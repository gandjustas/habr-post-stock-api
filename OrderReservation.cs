using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using ReserveKey = (int ItemId, int WarehouseId);


class OrderReservation(
    IProducer<ReserveKey, ReserveRequestMessage> producer, 
    IServiceScopeFactory scopeFactory,
    [FromKeyedServices("instanceId")]string instanceId,
    IAdminClient kafkaAdmin
    ): BackgroundService
{
    private const int ConsumerCount = 4;

    private readonly ConcurrentDictionary<(Guid OderId, int ItemId, int WarehouseId), TaskCompletionSource>  completions = [];

    public async Task Reserve(Order order, CancellationToken cancellationToken)
    {
        var tasks = order.Lines
            .Select(l => (order.Id, l.ItemId, l.WarehouseId))
            .ToDictionary(x=>x, x => completions.GetOrAdd(x, _ => new TaskCompletionSource()).Task)
            ;
        await Task.WhenAll(order.Lines.Select(l => producer.ProduceAsync(ReservationProcessor.Topic, new()
            {                    
                Key = (l.ItemId, l.WarehouseId),
                Value  = new ()
                {                        
                    OrderId = order.Id,
                    Quantity = l.Quantity,
                    ReplyTo = instanceId
                }
            }, cancellationToken)));
        try
        {
            await Task.WhenAll(tasks.Values);            
        }
        finally
        {
            foreach (var k in tasks.Keys)
            {
                completions.TryRemove(k, out _);
            }
        }
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await kafkaAdmin.CreateTopicsAsync([new TopicSpecification
            {
                Name = instanceId,
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
            await Task.Factory.StartNew(() => Worker(
                sp.GetRequiredService<IConsumer<Null, ReserveResponseMessage>>(),
                stoppingToken), TaskCreationOptions.LongRunning);
        }));

    }

    private void Worker(IConsumer<Null, ReserveResponseMessage> consumer, CancellationToken stoppingToken)
    {
        consumer.Subscribe(instanceId);
        while(!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            var m = result.Message.Value;

            if(completions.TryGetValue((m.OrderId, m.ItemId, m.WarehouseId), out var tcs))
            {
                if(m.IsError) tcs.TrySetException(new InvalidOperationException(m.ErrorMessage!));
                else tcs.TrySetResult();
            }
        }
    }
}