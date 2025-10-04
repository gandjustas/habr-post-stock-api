using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace stock_api.Migrations
{
    /// <inheritdoc />
    public partial class AddedCheckAndTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "check_stock",
                table: "stock",
                sql: "quantity >= reserved");

            migrationBuilder.Sql("CREATE FUNCTION \"LC_TRIGGER_AFTER_INSERT_ORDERLINE\"() RETURNS trigger as $LC_TRIGGER_AFTER_INSERT_ORDERLINE$\r\nBEGIN\r\n  UPDATE \"stock\"\r\n  SET \"reserved\" = \"stock\".\"reserved\" + NEW.\"quantity\"\r\n  WHERE \"stock\".\"item_id\" = NEW.\"item_id\" AND \"stock\".\"warehouse_id\" = NEW.\"warehouse_id\";\r\nRETURN NEW;\r\nEND;\r\n$LC_TRIGGER_AFTER_INSERT_ORDERLINE$ LANGUAGE plpgsql;\r\nCREATE TRIGGER LC_TRIGGER_AFTER_INSERT_ORDERLINE AFTER INSERT\r\nON \"order_lines\"\r\nFOR EACH ROW EXECUTE PROCEDURE \"LC_TRIGGER_AFTER_INSERT_ORDERLINE\"();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION \"LC_TRIGGER_AFTER_INSERT_ORDERLINE\"() CASCADE;");

            migrationBuilder.DropCheckConstraint(
                name: "check_stock",
                table: "stock");
        }
    }
}
