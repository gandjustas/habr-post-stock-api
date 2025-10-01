using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<StockApiDataContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
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
                select new OrderLine()
                {
                    ItemId = g.Key.ItemId,
                    WarehouseId = g.Key.WarehouseId,
                    Quantity = g.Aggregate(0, (s, l) => s + l.Quantity)
                };
    lines = lines.ToArray();

    order.Lines.Clear();
    order.Lines.AddRange(lines);

    await using var t = await ctx.Database.BeginTransactionAsync(ct);
    ctx.Orders.Add(order);
    await ctx.SaveChangesAsync(ct);

    var q = from l in ctx.OrderLines
            where l.OrderId == order.Id
            join s in ctx.Stock
            on new { l.ItemId, l.WarehouseId } equals new { s.ItemId, s.WarehouseId }
            select new { s, l };
    await q.ExecuteUpdateAsync(setter => setter.SetProperty(x => x.s.Reserved, x => x.s.Reserved + x.l.Quantity), ct);
    await t.CommitAsync(ct);
    
})
.WithName("PlaceOrder");

app.Run();
