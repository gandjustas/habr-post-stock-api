using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<StockApiDataContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgOptions => npgOptions.EnableRetryOnFailure())
       .UseSnakeCaseNamingConvention()
);
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


app.MapPost("/place-order/", async (Order order, StockApiDataContext ctx, CancellationToken ct) =>
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

    var db = ctx.Database;
    await db.CreateExecutionStrategy().ExecuteInTransactionAsync(async ct =>
    {
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync(ct);

        await db.ExecuteSqlAsync($"""
        UPDATE stock s
        SET reserved = s.reserved + l.quantity
        FROM (SELECT s.item_id,s.warehouse_id,l.quantity
            FROM stock s 
            JOIN order_lines as l 
                ON (s.item_id,s.warehouse_id) = (l.item_id,l.warehouse_id)
            WHERE l.order_id = {order.Id}
            ORDER BY 1,2
            FOR NO KEY UPDATE OF s) l
        WHERE (s.item_id,s.warehouse_id) = (l.item_id,l.warehouse_id)
        """, ct);

    }, ct => Task.FromResult(false), ct);
    
})
.WithName("PlaceOrder");

app.Run();
