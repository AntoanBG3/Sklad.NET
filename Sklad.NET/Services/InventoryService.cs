using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sklad.Data;
using Sklad.Models;

namespace Sklad.Services;

public class InventoryService : IInventoryService
{
    private readonly SkladDbContext _db;

    public InventoryService(SkladDbContext db) => _db = db;

    public async Task<IEnumerable<Tire>> SearchAsync(
        string? sku, string? brand, string? model,
        int? width, int? profile, int? diameter,
        Season? season, TireType? type)
    {
        var q = _db.Tires.AsQueryable();
        if (!string.IsNullOrWhiteSpace(sku))    q = q.Where(t => t.Sku.Contains(sku));
        if (!string.IsNullOrWhiteSpace(brand))  q = q.Where(t => t.Brand.Contains(brand));
        if (!string.IsNullOrWhiteSpace(model))  q = q.Where(t => t.Model.Contains(model));
        if (width.HasValue)    q = q.Where(t => t.Width    == width);
        if (profile.HasValue)  q = q.Where(t => t.Profile  == profile);
        if (diameter.HasValue) q = q.Where(t => t.Diameter == diameter);
        if (season.HasValue)   q = q.Where(t => t.Season   == season);
        if (type.HasValue)     q = q.Where(t => t.Type     == type);
        return await q.OrderBy(t => t.Brand).ThenBy(t => t.Model).ToListAsync();
    }

    public async Task<IEnumerable<Tire>> GetLowStockAsync()
        => await _db.Tires
            .Where(t => t.Quantity <= t.MinStock)
            .OrderBy(t => t.Brand).ThenBy(t => t.Model)
            .ToListAsync();

    public async Task RegisterMovementAsync(
        int tireId, MovementType movementType, int quantity, string? note)
    {
        var tire = await _db.Tires.FindAsync(tireId)
            ?? throw new InvalidOperationException("Tire not found.");

        if (movementType != MovementType.Adjustment && quantity < 1)
            throw new InvalidOperationException("Quantity must be at least 1 for In/Out movements.");

        tire.Quantity = movementType switch
        {
            MovementType.In => tire.Quantity + quantity,
            MovementType.Out when tire.Quantity >= quantity => tire.Quantity - quantity,
            MovementType.Out => throw new InvalidOperationException(
                $"Insufficient stock. Available: {tire.Quantity}, requested: {quantity}."),
            MovementType.Adjustment => quantity,
            _ => throw new ArgumentOutOfRangeException(nameof(movementType))
        };

        _db.StockMovements.Add(new StockMovement
        {
            TireId   = tireId,
            MovementType = movementType,
            Quantity = quantity,
            Note     = note,
            Date     = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    public Task<byte[]> ExportCsvAsync(IEnumerable<Tire> tires)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SKU,Brand,Model,Size,Season,Type,UnitPrice,Qty,MinStock,Location");
        foreach (var t in tires)
        {
            sb.AppendLine(
                $"{Csv(t.Sku)},{Csv(t.Brand)},{Csv(t.Model)},{Csv(t.Size)}," +
                $"{t.Season},{t.Type},{t.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)},{t.Quantity},{t.MinStock},{Csv(t.Location ?? "")}");
        }
        return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string Csv(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
}
