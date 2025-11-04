using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace stock_api.Migrations
{
    /// <inheritdoc />
    public partial class SqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stock",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Reserved = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stock", x => new { x.ItemId, x.WarehouseId });
                    table.CheckConstraint("check_stock", "Quantity >= Reserved");
                });

            migrationBuilder.CreateTable(
                name: "OrderLines",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLines", x => new { x.OrderId, x.ItemId, x.WarehouseId });
                    table.ForeignKey(
                        name: "FK_OrderLines_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("CREATE TRIGGER LC_TRIGGER_AFTER_INSERT_ORDERLINE ON \"OrderLines\" AFTER Insert AS\r\nBEGIN\r\n  DECLARE @NewQuantity INT, @NewItemId INT, @NewWarehouseId INT\r\n  DECLARE InsertedOrderLineCursor CURSOR LOCAL FOR SELECT Quantity, ItemId, WarehouseId FROM Inserted\r\n  OPEN InsertedOrderLineCursor\r\n  FETCH NEXT FROM InsertedOrderLineCursor INTO @NewQuantity, @NewItemId, @NewWarehouseId\r\n  WHILE @@FETCH_STATUS = 0\r\n  BEGIN\r\n    UPDATE \"Stock\"\r\n    SET \"Reserved\" = \"Stock\".\"Reserved\" + @NewQuantity\r\n    WHERE \"Stock\".\"ItemId\" = @NewItemId AND \"Stock\".\"WarehouseId\" = @NewWarehouseId;\r\n  FETCH NEXT FROM InsertedOrderLineCursor INTO @NewQuantity, @NewItemId, @NewWarehouseId\r\n  END\r\n  CLOSE InsertedOrderLineCursor DEALLOCATE InsertedOrderLineCursor\r\nEND");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER LC_TRIGGER_AFTER_INSERT_ORDERLINE;");

            migrationBuilder.DropTable(
                name: "OrderLines");

            migrationBuilder.DropTable(
                name: "Stock");

            migrationBuilder.DropTable(
                name: "Orders");
        }
    }
}
