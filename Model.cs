using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Laraue.EfCoreTriggers.Common.Extensions;
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

public class Order
{
    [Key]
    public Guid Id { get; set; }

    public List<int> ItemIds { get; set; } = [];
    public List<int> WarehouseIds { get; set; } = [];
    public List<int> Quantities { get; set; } = [];
}

public class StockApiDataContext(DbContextOptions<StockApiDataContext> options) : DbContext(options)
{
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<Stock> Stock { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tableBuilder = modelBuilder.Entity<Stock>();
        var reservedProp = tableBuilder.Property(s => s.Reserved).HasDefaultValue(0);
        var quantityProp = tableBuilder.Property(s => s.Quantity);


        tableBuilder.ToTable(t =>
            t.HasCheckConstraint("check_stock", $"{quantityProp.Metadata.GetColumnName()} >= {reservedProp.Metadata.GetColumnName()}"));

            
        modelBuilder.Entity<Order>()
                    .AfterInsert(t =>
                        t.Action(a =>
                            a.ExecuteRawSql("""
                                UPDATE stock
                                SET reserved = reserved + x.q
                                FROM (
                                    SELECT s.ctid, l.q
                                    FROM stock s 
                                    JOIN unnest({0},{1},{2}) AS l(i,w,q)
                                        on (s.item_id, s.warehouse_id) = (l.i,l.w)
                                    FOR NO KEY UPDATE
                                ) x
                                WHERE stock.ctid = x.ctid;
                            """,
                            tableRef => tableRef.New.ItemIds,
                            tableRef => tableRef.New.WarehouseIds,
                            tableRef => tableRef.New.Quantities
                            )
                        )
                    );
    }


}