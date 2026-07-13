using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sklad.Controllers;
using Sklad.Data;
using Sklad.Models;

namespace Sklad.Tests;

public class MaintenanceControllerTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Backup_streams_a_nonempty_sqlite_snapshot()
    {
        await using var context = _db.CreateContext();
        var controller = new MaintenanceController(context)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Backup();

        var file = Assert.IsType<FileContentResult>(result);
        Assert.StartsWith("sklad-backup-", file.FileDownloadName);
        Assert.EndsWith(".db", file.FileDownloadName);
        // Every SQLite database file starts with this magic string.
        Assert.Equal("SQLite format 3", System.Text.Encoding.ASCII.GetString(file.FileContents, 0, 15));
    }
}

public class MigrationDriftTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Model_has_no_pending_migration_changes()
    {
        // TestDb uses EnsureCreated, so migrations are never exercised by other
        // tests; this catches a model change committed without a migration.
        using var context = _db.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
    }
}

public class MigrationUpgradeTests
{
    private const string PreviousMigration = "20260711063455_ShopPreferences";

    [Fact]
    public async Task Inventory_integrity_migration_preserves_related_data_and_enforces_unicode_case()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sklad-migration-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<SkladDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .AddInterceptors(new SqliteFunctionsInterceptor())
                .Options;

            await using (var context = new SkladDbContext(options))
            {
                var migrator = context.GetService<IMigrator>();
                await migrator.MigrateAsync(PreviousMigration);

                await context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO Suppliers (Name) VALUES ('Гуми Трейд');
                    INSERT INTO Users (Username, PasswordHash, Role, SecurityStamp, CreatedAt)
                        VALUES ('Админ', 'hash', 0, 'stamp', '2026-07-12 10:00:00');
                    INSERT INTO Tires
                        (Sku, Barcode, Brand, Model, Width, Profile, Diameter, Season, Type,
                         UnitPrice, Quantity, MinStock, Location, Version)
                        VALUES ('ГУМА-1', 'КОД-1', 'Марка', 'Модел', 205, 55, 16, 0, 0,
                                '100.00', 5, 2, 'A-1', 0);
                    INSERT INTO PurchaseOrders
                        (SupplierId, Status, CreatedAt, Note)
                        VALUES (1, 0, '2026-07-12 10:00:00', 'keep me');
                    INSERT INTO PurchaseOrderItems (PurchaseOrderId, TireId, Quantity, UnitCost)
                        VALUES (1, 1, 2, '90.00');
                    INSERT INTO StockMovements
                        (TireId, MovementType, Quantity, Date, Note, UserName)
                        VALUES (1, 0, 5, '2026-07-12 10:00:00', 'opening', 'Админ');
                    """);

                await migrator.MigrateAsync();
            }

            await using (var check = new SkladDbContext(options))
            {
                var tire = await check.Tires.SingleAsync(tire => tire.Sku == "гума-1");
                Assert.Equal("КОД-1", tire.Barcode);
                Assert.Single(await check.StockMovements.Where(movement => movement.TireId == tire.Id).ToListAsync());

                var order = await check.PurchaseOrders.Include(order => order.Items).SingleAsync();
                Assert.Equal("keep me", order.Note);
                Assert.Equal(0, order.Version);
                Assert.Single(order.Items);

                check.Users.Add(new AppUser
                {
                    Username = "аДМИН",
                    PasswordHash = "hash",
                    Role = UserRole.Admin,
                    SecurityStamp = "new-stamp"
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Unicode_collision_aborts_before_schema_rebuild_and_is_retryable_after_repair()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sklad-collision-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<SkladDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .AddInterceptors(new SqliteFunctionsInterceptor())
            .Options;

        try
        {
            await using (var setup = new SkladDbContext(options))
            {
                await setup.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                await setup.Database.ExecuteSqlRawAsync("""
                    INSERT INTO Users (Username, PasswordHash, Role, SecurityStamp, CreatedAt)
                        VALUES ('Админ', 'hash', 0, 'stamp-1', '2026-07-12 10:00:00');
                    INSERT INTO Users (Username, PasswordHash, Role, SecurityStamp, CreatedAt)
                        VALUES ('аДМИН', 'hash', 0, 'stamp-2', '2026-07-12 10:00:00');
                    """);
            }

            await using (var failingUpgrade = new SkladDbContext(options))
            {
                var error = await Assert.ThrowsAsync<SqliteException>(
                    () => failingUpgrade.GetService<IMigrator>().MigrateAsync());
                Assert.Equal(19, error.SqliteErrorCode);

                var connection = failingUpgrade.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                    await connection.OpenAsync();
                await using var command = connection.CreateCommand();

                command.CommandText =
                    "SELECT COUNT(*) FROM pragma_table_info('PurchaseOrders') WHERE name = 'Version'";
                Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);

                command.CommandText =
                    "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260712195107_InventoryIntegrity'";
                Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
            }

            // An operator can rename/delete the conflicting old-schema record
            // and retry; no half-rebuilt tables need manual recovery.
            await using (var repair = new SkladDbContext(options))
            {
                await repair.Database.ExecuteSqlRawAsync("DELETE FROM Users WHERE Username = 'аДМИН'");
                await repair.GetService<IMigrator>().MigrateAsync();
            }

            await using (var check = new SkladDbContext(options))
            {
                Assert.Single(await check.Users.ToListAsync());
                Assert.False(check.Database.HasPendingModelChanges());
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
