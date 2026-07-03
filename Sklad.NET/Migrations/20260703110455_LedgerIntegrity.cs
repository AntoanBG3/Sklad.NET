using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sklad.Migrations
{
    /// <inheritdoc />
    public partial class LedgerIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Tires",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "StockMovements",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Tires");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "StockMovements");
        }
    }
}
