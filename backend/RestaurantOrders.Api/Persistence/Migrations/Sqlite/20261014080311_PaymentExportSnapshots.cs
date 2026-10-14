using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantOrders.Api.Persistence.Migrations.Sqlite
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
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ItemCode",
                table: "OrderUnits",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPriceBeforeVat",
                table: "OrderUnits",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Older units only saved the gross price. Recover net using the available catalog VAT.
            migrationBuilder.Sql("""
                UPDATE OrderUnits
                SET ItemCode = (SELECT Code FROM MenuItems WHERE Id = OrderUnits.MenuItemId),
                    CategoryCode = (SELECT c.Code FROM Categories c JOIN MenuItems m ON m.CategoryId = c.Id WHERE m.Id = OrderUnits.MenuItemId),
                    UnitPriceBeforeVat = CAST(ROUND(CAST(UnitPrice AS REAL) / (1 + (SELECT CAST(VatRate AS REAL) FROM MenuItems WHERE Id = OrderUnits.MenuItemId) / 100.0), 2) AS TEXT)
                """);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "OrderUnits",
                type: "TEXT",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
            migrationBuilder.Sql("UPDATE OrderUnits SET VatRate = (SELECT VatRate FROM MenuItems WHERE Id = OrderUnits.MenuItemId)");
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
