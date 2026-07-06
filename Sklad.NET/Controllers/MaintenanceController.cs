using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sklad.Data;
using Sklad.Models;

namespace Sklad.Controllers;

[Authorize(Roles = nameof(UserRole.Admin))]
public class MaintenanceController : Controller
{
    private readonly SkladDbContext _db;

    public MaintenanceController(SkladDbContext db) => _db = db;

    // POST: /Maintenance/Backup — POST so antiforgery applies and crawlers or
    // prefetchers can't trigger disk writes. VACUUM INTO produces a consistent
    // snapshot without stopping the app.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Backup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sklad-backup-{Guid.NewGuid():N}.db");
        try
        {
            await _db.Database.ExecuteSqlAsync($"VACUUM INTO {path}");
            var bytes = await System.IO.File.ReadAllBytesAsync(path);
            return File(bytes, "application/octet-stream", $"sklad-backup-{DateTime.UtcNow:yyyyMMdd-HHmm}.db");
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }
}
