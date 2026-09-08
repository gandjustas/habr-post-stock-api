using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using ReserveKey = (int ItemId, int WarehouseId);

internal class ReserveQueue(IServiceScopeFactory scopeFactory) : BackgroundService
{
    private readonly Channel<QueueItem> channel = Channel.CreateUnbounded<QueueItem>(new() { SingleReader = true });
    readonly ConcurrentDictionary<ReserveKey, int> reservesCache = [];
    readonly ConcurrentDictionary<ReserveKey, int> limitsCache = [];

    public async Task Reserve(Order order, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        await channel.Writer.WriteAsync(new(tcs, order, cancellationToken), cancellationToken);
        await tcs.Task;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var ctx = sp.GetRequiredService<StockApiDataContext>();

        List<int> itemIds = [];
        List<int> warehouseIds = [];
        List<int> quantities = [];
        List<Guid> orderIds = [];
        List<TaskCompletionSource> completions = [];

        var reader = channel.Reader;

        while (await reader.WaitToReadAsync(stoppingToken))
        {
            while (reader.TryRead(out var item))
            {
                var (tcs, order, token) = item;
                if (token.IsCancellationRequested) continue;

                foreach (var line in order.Lines)
                {
                    var key = (line.ItemId, line.WarehouseId);
                    itemIds.Add(line.ItemId);
                    warehouseIds.Add(line.WarehouseId);
                    quantities.Add(line.Quantity);
                    orderIds.Add(order.Id);

                    while (true)
                    {
                        if (!reservesCache.TryGetValue(key, out var sum))
                        {
                            sum = await ctx.Operations
                                .Where(o => o.ItemId == line.ItemId && o.WarehouseId == line.WarehouseId)
                                .SumAsync(o => o.Quantity, stoppingToken);
                            reservesCache.TryAdd(key, sum);
                        }

                        if (!limitsCache.TryGetValue(key, out var limit))
                        {
                            limit = await ctx.Stock
                                .Where(o => o.ItemId == line.ItemId && o.WarehouseId == line.WarehouseId)
                                .Select(s => s.Quantity)
                                .FirstAsync(stoppingToken);
                            limitsCache.TryAdd(key, limit);
                        }

                        if (sum + line.Quantity > limit)
                        {
                            tcs.TrySetException(new InvalidOperationException("Not enough stock"));
                        }
                        else
                        {
                            if (reservesCache.TryUpdate(key, sum + line.Quantity, sum))
                            {
                                itemIds.Add(line.ItemId);
                                warehouseIds.Add(line.WarehouseId);
                                quantities.Add(line.Quantity);
                                orderIds.Add(order.Id);
                                completions.Add(item.Source);
                                break;
                            }
                        }
                    }
                }
            }

            try
            {
                await ctx.Database.ExecuteSqlAsync($"""
                    INSERT INTO operations(item_id, warehouse_id, order_id, quantity)
                    SELECT * FROM unnest({itemIds}, {warehouseIds}, {orderIds}, {quantities}) as t(item_id, warehouse_id, order_id, quantity)
                """, stoppingToken);

                foreach (var c in completions)
                {
                    c.TrySetResult();
                }
                
            }
            catch(Exception ex)
            {
                foreach (var c in completions)
                {
                    c.TrySetException(ex);
                }                
            }

            itemIds.Clear();
            warehouseIds.Clear();
            quantities.Clear();
            orderIds.Clear();
            completions.Clear();
        }

    }

    private readonly record struct QueueItem(TaskCompletionSource Source, Order Order, CancellationToken CancellationToken);
}