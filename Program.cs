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


app.MapPut("/place-order/{id}", async (Guid id, ICollection<OrderLineModel> lines, DbQueueService<StockApiDataContext> worker, CancellationToken ct) =>
{
    var q = from l in lines
            group l by new { l.ItemId, l.WarehouseId } into g
            orderby g.Key.ItemId, g.Key.WarehouseId
            select new
            {
                g.Key.ItemId,
                g.Key.WarehouseId,
                Quantity = g.Aggregate(0, (s, l) => s + l.Quantity)
            };

    List<int> itemIds = [], warehouseIds = [], quantities = [];
    foreach (var l in q)
    {
        itemIds.Add(l.ItemId);
        warehouseIds.Add(l.WarehouseId);
        quantities.Add(l.Quantity);
    }
    await worker.ExecuteAsync(async (ctx, ct) =>
    {
        var db = ctx.Database;
        await db.CreateExecutionStrategy().ExecuteInTransactionAsync(async ct =>
        {
            await db.ExecuteSqlAsync($"""
                INSERT INTO orders 
                VALUES({id},{itemIds},{warehouseIds},{quantities}) 
                ON CONFLICT (id) DO NOTHING
                """, ct);
        }, ct => Task.FromResult(false), ct);
    }, ct);    

})
.WithName("PlaceOrder");

app.Run();

public record OrderLineModel(int ItemId, int WarehouseId, int Quantity);