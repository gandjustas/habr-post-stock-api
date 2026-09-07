using System.Collections.Concurrent;
using Confluent.Kafka;
using ReserveKey = (int ItemId, int WarehouseId);


class OrderReservation(
    IProducer<ReserveKey, ReserveRequestMessage> producer, 
    IConsumer<Null, ReserveResponseMessage> consumer,
    [FromKeyedServices("instanceId")]string instanceId
    ): BackgroundService
{
    private readonly ConcurrentDictionary<(Guid OderId, int ItemId, int WarehouseId), TaskCompletionSource>  completions = [];

    public async Task Reserve(Order order, CancellationToken cancellationToken)
    {
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
            await Task.WhenAll(order.Lines.Select(l => completions.GetOrAdd((order.Id, l.ItemId, l.WarehouseId), _ => new TaskCompletionSource()).Task));            
        }
        finally
        {
            foreach (var l in order.Lines)
            {
                completions.TryRemove((order.Id, l.ItemId, l.WarehouseId), out _);
            }
        }
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        consumer.Subscribe(instanceId);
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {                
                var result = consumer.Consume(stoppingToken);
                var m = result.Message.Value;

                var tcs = completions.GetOrAdd((m.OrderId, m.ItemId, m.WarehouseId), _ => new TaskCompletionSource());
                if(m.IsError) tcs.TrySetException(new InvalidOperationException(m.ErrorMessage!));
                else tcs.TrySetResult();
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}