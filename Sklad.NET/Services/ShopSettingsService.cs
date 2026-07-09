using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sklad.Data;
using Sklad.Models;

namespace Sklad.Services;

public class ShopSettingsService : IShopSettingsService
{
    private readonly SkladDbContext _db;
    private readonly ILogger<ShopSettingsService> _logger;

    public ShopSettingsService(SkladDbContext db, ILogger<ShopSettingsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // No migration seeds the row, so an unconfigured install must read as blank
    // rather than null. Callers render a letterhead only for the fields set.
    public async Task<ShopSettings> GetAsync() =>
        await _db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == ShopSettings.SingletonId)
        ?? new ShopSettings { Id = ShopSettings.SingletonId };

    public async Task SaveAsync(ShopSettings settings)
    {
        var existing = await _db.ShopSettings.FirstOrDefaultAsync(s => s.Id == ShopSettings.SingletonId);
        if (existing is null)
        {
            settings.Id = ShopSettings.SingletonId;
            _db.ShopSettings.Add(settings);
        }
        else
        {
            existing.Name = settings.Name;
            existing.Address = settings.Address;
            existing.VatNumber = settings.VatNumber;
            existing.Phone = settings.Phone;
            existing.Email = settings.Email;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Shop settings saved.");
    }
}
