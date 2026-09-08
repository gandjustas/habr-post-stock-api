using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using ReserveKey = (int ItemId, int WarehouseId);

class ReservationProcessor(
    IProducer<Null, ReserveResponseMessage> producer,
    IAdminClient kafkaAdmin,
    IServiceScopeFactory scopeFactory
    ) : BackgroundService
{
    public const string Topic = "reservations";

    private const int ConsumerCount = 4;

    readonly ConcurrentDictionary<ReserveKey, int> reservesCache = [];
    readonly ConcurrentDictionary<ReserveKey, int> limitsCache = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await kafkaAdmin.CreateTopicsAsync([new TopicSpecification
            {
                Name = Topic,
                NumPartitions = ConsumerCount,
            }]);
            
        }
        catch (CreateTopicsException  e) when (e.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
        {
        }

        await Task.WhenAll(Enumerable.Range(1, ConsumerCount)
        .Select(async i =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            await Task.Run(() => Worker(
                sp.GetRequiredService<IConsumer<ReserveKey, ReserveRequestMessage>>(),
                sp.GetRequiredService<StockApiDataContext>(),
                stoppingToken), stoppingToken);
        }));
    }

    async Task Worker(IConsumer<ReserveKey, ReserveRequestMessage> consumer, StockApiDataContext ctx, CancellationToken cancellationToken)
    {
        List<Message<ReserveKey, ReserveRequestMessage>> incomingBatch = [];
        List<(string ReplyTo, ReserveResponseMessage Message)> outgoingBatch = [];

        List<int> itemIds = [];
        List<int> warehouseIds = [];
        List<int> quantities = [];
        List<Guid> orderIds = [];

        consumer.Subscribe(Topic);
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = consumer.Consume(cancellationToken);
            incomingBatch.Add(result.Message);
            while ((result = consumer.Consume(TimeSpan.Zero)) != null) incomingBatch.Add(result.Message);
            
            foreach (var message in incomingBatch)
            {
                var (itemId, warehouseId) = message.Key;
                var m = message.Value;

                ReserveResponseMessage response = new()
                {
                    OrderId = m.OrderId,
                    ItemId = itemId,
                    WarehouseId = warehouseId,
                    IsError = false
                };

                if (!reservesCache.TryGetValue(message.Key, out var sum))
                {
                    sum = await ctx.Operations
                        .Where(o => o.ItemId == itemId && o.WarehouseId == warehouseId)
                        .SumAsync(o => o.Quantity, cancellationToken);
                    reservesCache.TryAdd(message.Key, sum);
                }

                if (!limitsCache.TryGetValue(message.Key, out var limit))
                {
                    limit = await ctx.Stock
                        .Where(s => s.ItemId == itemId && s.WarehouseId == warehouseId)
                        .Select(s => s.Quantity)
                        .FirstAsync(cancellationToken);
                    limitsCache.TryAdd(message.Key, limit);
                }


                if (sum + m.Quantity > limit)
                {
                    response.IsError = true;
                    response.ErrorMessage = "Not enough stock";
                }
                else
                {
                    itemIds.Add(itemId);
                    warehouseIds.Add(warehouseId);
                    quantities.Add(m.Quantity);
                    orderIds.Add(m.OrderId);
                }
                outgoingBatch.Add((m.ReplyTo, response));
            }

            await ctx.Database.ExecuteSqlAsync($"""
                INSERT INTO operations(item_id, warehouse_id, order_id, quantity)
                SELECT * FROM unnest({itemIds}, {warehouseIds}, {orderIds}, {quantities}) as t(item_id, warehouse_id, order_id, quantity)
            """, cancellationToken);

            itemIds.Clear();
            warehouseIds.Clear();
            quantities.Clear();
            orderIds.Clear();

            incomingBatch.Clear();

            await Task.WhenAll(outgoingBatch.Select(p => 
                producer.ProduceAsync(p.ReplyTo, new () { Value = p.Message}, cancellationToken)));
            outgoingBatch.Clear();
            consumer.Commit();
        }


    }
}
