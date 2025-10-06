using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Laraue.EfCoreTriggers.PostgreSql.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<StockApiDataContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
            npgOptions => npgOptions.EnableRetryOnFailure())
       .UseSnakeCaseNamingConvention()
       .UsePostgreSqlTriggers()
);
builder.Services.Configure<DbQueueServiceOptions>(o => { });
builder.Services.AddSingleton<DbQueueService<StockApiDataContext>>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DbQueueService<StockApiDataContext>>());

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


app.MapPost("/place-order/", async (OrderModel order, DbQueueService<StockApiDataContext> worker, CancellationToken ct) =>
{
    var lines = from l in order.Lines
                group l by new { l.ItemId, l.WarehouseId } into g
                orderby g.Key.ItemId, g.Key.WarehouseId
                select new
                {
                    g.Key.ItemId,
                    g.Key.WarehouseId,
                    Quantity = g.Aggregate(0, (s, l) => s + l.Quantity)
                };

    Order dbOrder = new() { Id = order.Id };
    foreach (var l in lines)
    {
        dbOrder.ItemIds.Add(l.ItemId);
        dbOrder.WarehouseIds.Add(l.WarehouseId);
        dbOrder.Quantities.Add(l.Quantity);
    }
    await worker.ExecuteAsync(async (ctx, ct) =>
    {
        ctx.Orders.Add(dbOrder);
        await ctx.SaveChangesAsync(ct);
    }, ct);    

})
.WithName("PlaceOrder");

app.Run();

public record OrderModel(Guid Id, ICollection<OrderLineModel> Lines);
public record OrderLineModel(int ItemId, int WarehouseId, int Quantity);