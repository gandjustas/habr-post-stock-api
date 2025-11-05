using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace stock_api.Migrations
{
    /// <inheritdoc />
    public partial class CheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "check_stock",
                table: "stock",
                sql: "quantity >= reserved");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "check_stock",
                table: "stock");
        }
    }
}
