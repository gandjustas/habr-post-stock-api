using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;


[PrimaryKey(nameof(ItemId), nameof(WarehouseId))]
public class Stock
{
    public int ItemId { get; set; }
    public int WarehouseId { get; set; }
    public int Quantity { get; set; }
    [DefaultValue(0)]
    public int Reserved { get; set; }

}

[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public class Order
{
    [Key]
    public Guid Id { get; set; }

    public List<OrderLine> Lines { get; } = new List<OrderLine>();
}

[PrimaryKey(nameof(OrderId), nameof(ItemId), nameof(WarehouseId))]
public class OrderLine
{
    [JsonIgnore]
    public Guid OrderId { get; set; }
    public int ItemId { get; set; }
    public int WarehouseId { get; set; }
    public int Quantity { get; set; }

    [JsonIgnore]
    public bool IsReserved { get; set; }

}

public class StockApiDataContext(DbContextOptions<StockApiDataContext> options) : DbContext(options)
{
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderLine> OrderLines { get; set; } = null!;
    public DbSet<Stock> Stock { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tableBuilder = modelBuilder.Entity<Stock>();
        var reservedProp = tableBuilder.Property(s => s.Reserved).HasDefaultValue(0);
        var quantityProp = tableBuilder.Property(s => s.Quantity);

        tableBuilder.ToTable(t =>
            t.HasCheckConstraint("check_stock", $"{quantityProp.Metadata.GetColumnName()} >= {reservedProp.Metadata.GetColumnName()}"));
    }


}