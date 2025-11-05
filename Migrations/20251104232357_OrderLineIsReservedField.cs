using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace stock_api.Migrations
{
    /// <inheritdoc />
    public partial class OrderLineIsReservedField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_reserved",
                table: "order_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_reserved",
                table: "order_lines");
        }
    }
}
