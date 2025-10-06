using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace stock_api.Migrations
{
    /// <inheritdoc />
    public partial class OptimizedStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_ids = table.Column<List<int>>(type: "integer[]", nullable: false),
                    warehouse_ids = table.Column<List<int>>(type: "integer[]", nullable: false),
                    quantities = table.Column<List<int>>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock",
                columns: table => new
                {
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    warehouse_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    reserved = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock", x => new { x.item_id, x.warehouse_id });
                    table.CheckConstraint("check_stock", "quantity >= reserved");
                });

            migrationBuilder.Sql("CREATE FUNCTION \"LC_TRIGGER_AFTER_INSERT_ORDER\"() RETURNS trigger as $LC_TRIGGER_AFTER_INSERT_ORDER$\r\nBEGIN\r\n      UPDATE stock\r\n    SET reserved = reserved + x.q\r\n    FROM (\r\n        SELECT s.ctid, l.q\r\n        FROM stock s \r\n        JOIN unnest(NEW.\"item_ids\",NEW.\"warehouse_ids\",NEW.\"quantities\") AS l(i,w,q)\r\n            on (s.item_id, s.warehouse_id) = (l.i,l.w)\r\n        FOR NO KEY UPDATE\r\n    ) x\r\n    WHERE stock.ctid = x.ctid;\r\nRETURN NEW;\r\nEND;\r\n$LC_TRIGGER_AFTER_INSERT_ORDER$ LANGUAGE plpgsql;\r\nCREATE TRIGGER LC_TRIGGER_AFTER_INSERT_ORDER AFTER INSERT\r\nON \"orders\"\r\nFOR EACH ROW EXECUTE PROCEDURE \"LC_TRIGGER_AFTER_INSERT_ORDER\"();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION \"LC_TRIGGER_AFTER_INSERT_ORDER\"() CASCADE;");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "stock");
        }
    }
}
