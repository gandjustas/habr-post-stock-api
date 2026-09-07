using Confluent.Kafka;
using Microsoft.AspNetCore.Routing.Tree;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReserveKey = (int ItemId, int WarehouseId);

class ReservationProcessor(
    IProducer<Null, ReserveResponseMessage> producer,
    IServiceScopeFactory scopeFactory
    ) : BackgroundService
{
    public const string Topic = "reservations";
    private const int ConsumerCount = 1;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {

        return Task.WhenAll(Enumerable.Range(1, ConsumerCount)
        .Select(async i =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            await Worker(
                sp.GetRequiredService<IConsumer<ReserveKey, ReserveRequestMessage>>(), 
                sp.GetRequiredService<StockApiDataContext>(), 
                stoppingToken);
        }));
    }

    async Task Worker(IConsumer<ReserveKey, ReserveRequestMessage> consumer, StockApiDataContext ctx, CancellationToken cancellationToken)
    {
        consumer.Subscribe(Topic);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(cancellationToken);
                var (ItemId, WarehouseId) = result.Message.Key;
                var m = result.Message.Value;

                ReserveResponseMessage response = new()
                {
                    OrderId = m.OrderId,
                    ItemId = ItemId,
                    WarehouseId = WarehouseId,
                    IsError = false
                };

                try
                {
                    await ctx.Stock
                        .Where(s => s.ItemId == ItemId && s.WarehouseId == WarehouseId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Reserved, s => s.Reserved + m.Quantity), cancellationToken);
                }
                catch (Exception ex)
                {
                    response.IsError = true;
                    response.ErrorMessage = ex.Message;
                }
                await producer.ProduceAsync(m.ReplyTo, new() { Value = response }, cancellationToken);
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }


    }
}
