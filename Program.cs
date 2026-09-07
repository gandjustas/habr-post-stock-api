using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReserveKey = (int ItemId, int WarehouseId);


var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;
services.AddOpenApi();
services.AddDbContext<StockApiDataContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgOptions => npgOptions.EnableRetryOnFailure())
       .UseSnakeCaseNamingConvention()
);

var configuration = builder.Configuration;

services.Configure<ProducerConfig>(configuration.GetSection("Kafka:Producer"));
services.Configure<ConsumerConfig>(configuration.GetSection("Kafka:Consumer"));

var instanceId = $"reply-{Environment.MachineName}";// $"reply-{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";
services.AddKeyedSingleton("instanceId", instanceId);

services.AddSingleton(typeof(ISerializer<>),typeof(KafkaMemoryPackSerDes<>));
services.AddSingleton(typeof(IDeserializer<>),typeof(KafkaMemoryPackSerDes<>));

services.AddSingleton(sp => 
    new ProducerBuilder<ReserveKey, ReserveRequestMessage>(sp.GetRequiredService<IOptions<ProducerConfig>>().Value)
    .SetKeySerializer(sp.GetRequiredService<ISerializer<ReserveKey>>())
    .SetValueSerializer(sp.GetRequiredService<ISerializer<ReserveRequestMessage>>())
    .Build());

services.AddSingleton(sp => 
    new ProducerBuilder<Null, ReserveResponseMessage>(sp.GetRequiredService<IOptions<ProducerConfig>>().Value)
    .SetValueSerializer(sp.GetRequiredService<ISerializer<ReserveResponseMessage>>())
    .Build());

services.AddTransient(sp => 
    new ConsumerBuilder<ReserveKey, ReserveRequestMessage>(new ConsumerConfig(sp.GetRequiredService<IOptions<ConsumerConfig>>().Value)
    {
        GroupId = "stock" // one goup for all
    })
    .SetKeyDeserializer(sp.GetRequiredService<IDeserializer<ReserveKey>>())
    .SetValueDeserializer(sp.GetRequiredService<IDeserializer<ReserveRequestMessage>>())
    .Build());


services.AddTransient(sp => 
    new ConsumerBuilder<Null, ReserveResponseMessage>(new ConsumerConfig(sp.GetRequiredService<IOptions<ConsumerConfig>>().Value)
    {
        GroupId = sp.GetRequiredKeyedService<string>("instanceId")
    })
    .SetValueDeserializer(sp.GetRequiredService<IDeserializer<ReserveResponseMessage>>())
    .Build());

services.AddSingleton<OrderReservation>();
services.AddHostedService(sp => sp.GetRequiredService<OrderReservation>());
services.AddHostedService<ReservationProcessor>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StockApiDataContext>();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

//app.UseHttpsRedirection();


app.MapPost("/place-order/", async (Order order, StockApiDataContext ctx, OrderReservation reservation, CancellationToken ct) =>
{
    var lines = from l in order.Lines
                group l by new { l.ItemId, l.WarehouseId } into g
                orderby g.Key.ItemId, g.Key.WarehouseId
                select new OrderLine()
                {
                    ItemId = g.Key.ItemId,
                    WarehouseId = g.Key.WarehouseId,
                    Quantity = g.Aggregate(0, (s, l) => s + l.Quantity)
                };
    lines = [.. lines];

    order.Lines.Clear();
    order.Lines.AddRange(lines);
    ctx.Orders.Add(order);
    await ctx.SaveChangesAsync(ct);

    await reservation.Reserve(order, ct);    
})
.WithName("PlaceOrder");

app.Run();
