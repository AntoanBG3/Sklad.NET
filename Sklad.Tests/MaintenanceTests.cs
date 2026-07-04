using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sklad.Controllers;

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
