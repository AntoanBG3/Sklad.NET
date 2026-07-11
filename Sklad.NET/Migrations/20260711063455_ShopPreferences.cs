using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sklad.Migrations
{
    /// <inheritdoc />
    public partial class ShopPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultCulture",
                table: "ShopSettings",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultMinStock",
                table: "ShopSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PageSize",
                table: "ShopSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportRangeMonths",
                table: "ShopSettings",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultCulture",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "DefaultMinStock",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "PageSize",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "ReportRangeMonths",
                table: "ShopSettings");
        }
    }
}
