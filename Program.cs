using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<StockApiDataContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
       .UseSnakeCaseNamingConvention()
);
builder.Services.AddSingleton(TimeProvider.System);
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

    try
    {
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync(ct);
        foreach (var l in order.Lines)
        {
            await using (var t = await ctx.Database.BeginTransactionAsync(ct))
            {
                await ctx.Stock
                        .Where(s => s.ItemId == l.ItemId && s.WarehouseId == l.WarehouseId)
                        .ExecuteUpdateAsync(setter =>
                            setter.SetProperty(
                                s => s.Reserved,
                                s => s.Reserved + l.Quantity),
                            ct);
                l.IsReserved = true;
                await ctx.SaveChangesAsync(ct);
                await t.CommitAsync(ct);
            }
        }
    }
    catch
    {
        // Rollback stock updates
        foreach (var l in order.Lines.Where(l => l.IsReserved))
        {
            await ctx.Stock
                    .Where(s => s.ItemId == l.ItemId && s.WarehouseId == l.WarehouseId)
                    .ExecuteUpdateAsync(setter =>
                        setter.SetProperty(
                            s => s.Reserved,
                            s => s.Reserved - l.Quantity),
                        CancellationToken.None);
        }
        ctx.Orders.Remove(order);
        await ctx.SaveChangesAsync(CancellationToken.None);

        throw;
    }
})
.WithName("PlaceOrder");

app.Run();
