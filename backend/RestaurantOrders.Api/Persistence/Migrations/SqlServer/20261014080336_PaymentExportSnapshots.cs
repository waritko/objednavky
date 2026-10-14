using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrders.Api.Persistence.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class PaymentExportSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryCode",
                table: "OrderUnits",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ItemCode",
                table: "OrderUnits",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPriceBeforeVat",
                table: "OrderUnits",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Older units only saved the gross price. Recover net using the available catalog VAT.
            migrationBuilder.Sql("""
                UPDATE u SET ItemCode = m.Code, CategoryCode = c.Code,
                    UnitPriceBeforeVat = ROUND(u.UnitPrice / (1 + m.VatRate / 100.0), 2)
                FROM OrderUnits u JOIN MenuItems m ON m.Id = u.MenuItemId
                JOIN Categories c ON c.Id = m.CategoryId
                """);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "OrderUnits",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
            migrationBuilder.Sql("UPDATE u SET VatRate = m.VatRate FROM OrderUnits u JOIN MenuItems m ON m.Id = u.MenuItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryCode",
                table: "OrderUnits");

            migrationBuilder.DropColumn(
                name: "ItemCode",
                table: "OrderUnits");

            migrationBuilder.DropColumn(
                name: "UnitPriceBeforeVat",
                table: "OrderUnits");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "OrderUnits");
        }
    }
}
