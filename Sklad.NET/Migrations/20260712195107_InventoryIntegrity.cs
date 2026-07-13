using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sklad.Migrations
{
    /// <inheritdoc />
    public partial class InventoryIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The old ASCII-only NOCASE indexes allowed Cyrillic case-only pairs.
            // Prove the existing data is compatible before SQLite starts any
            // non-transactional table rebuild. IF NOT EXISTS keeps a failed
            // upgrade retryable if an earlier preflight index was committed.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Preflight_Users_Username_Unicode"
                    ON "Users" ("Username" COLLATE UNICODE_NOCASE);
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Preflight_Tires_Sku_Unicode"
                    ON "Tires" ("Sku" COLLATE UNICODE_NOCASE);
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Preflight_Suppliers_Name_Unicode"
                    ON "Suppliers" ("Name" COLLATE UNICODE_NOCASE);
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                collation: "UNICODE_NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 100,
                oldCollation: "NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "Tires",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                collation: "UNICODE_NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldCollation: "NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "Barcode",
                table: "Tires",
                type: "TEXT",
                maxLength: 100,
                nullable: true,
                collation: "UNICODE_NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 100,
                oldNullable: true,
                oldCollation: "NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Suppliers",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                collation: "UNICODE_NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 200,
                oldCollation: "NOCASE");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "PurchaseOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "PurchaseOrders");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 100,
                oldCollation: "UNICODE_NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "Tires",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldCollation: "UNICODE_NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "Barcode",
                table: "Tires",
                type: "TEXT",
                maxLength: 100,
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 100,
                oldNullable: true,
                oldCollation: "UNICODE_NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Suppliers",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 200,
                oldCollation: "UNICODE_NOCASE");
        }
    }
}
