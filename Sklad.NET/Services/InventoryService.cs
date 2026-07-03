using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sklad.Data;
using Sklad.Models;
using Sklad.ViewModels;

namespace Sklad.Services;

public class InventoryService : IInventoryService
{
    public const int DefaultPageSize = 50;
    private const int ConcurrencyRetries = 3;

    private readonly SkladDbContext _db;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(SkladDbContext db, ILogger<InventoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<Tire>> SearchAsync(TireFilterViewModel filter, int pageSize = DefaultPageSize)
    {
        var q = _db.Tires.AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Sku))     q = q.Where(t => t.Sku.Contains(filter.Sku));
        if (!string.IsNullOrWhiteSpace(filter.Barcode)) q = q.Where(t => t.Barcode == filter.Barcode);
        if (!string.IsNullOrWhiteSpace(filter.Brand))   q = q.Where(t => t.Brand.Contains(filter.Brand));
        if (!string.IsNullOrWhiteSpace(filter.Model))   q = q.Where(t => t.Model.Contains(filter.Model));
        if (filter.Width.HasValue)    q = q.Where(t => t.Width    == filter.Width);
        if (filter.Profile.HasValue)  q = q.Where(t => t.Profile  == filter.Profile);
        if (filter.Diameter.HasValue) q = q.Where(t => t.Diameter == filter.Diameter);
        if (filter.Season.HasValue)   q = q.Where(t => t.Season   == filter.Season);
        if (filter.Type.HasValue)     q = q.Where(t => t.Type     == filter.Type);

        q = ApplySort(q, filter.Sort);

        var page = Math.Max(1, filter.Page);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<Tire>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    private static IQueryable<Tire> ApplySort(IQueryable<Tire> q, string? sort) => sort switch
    {
        "sku"    => q.OrderBy(t => t.Sku),
        "-sku"   => q.OrderByDescending(t => t.Sku),
        // Cast to double: SQLite decimal ORDER BY parses text with the current culture and breaks under bg-BG.
        "price"  => q.OrderBy(t => (double)t.UnitPrice),
        "-price" => q.OrderByDescending(t => (double)t.UnitPrice),
        "qty"    => q.OrderBy(t => t.Quantity),
        "-qty"   => q.OrderByDescending(t => t.Quantity),
        "size"   => q.OrderBy(t => t.Width).ThenBy(t => t.Profile).ThenBy(t => t.Diameter),
        "-brand" => q.OrderByDescending(t => t.Brand).ThenByDescending(t => t.Model),
        _        => q.OrderBy(t => t.Brand).ThenBy(t => t.Model)
    };

    public async Task<IEnumerable<Tire>> GetLowStockAsync()
        => await _db.Tires
            .Where(t => t.Quantity <= t.MinStock)
            .OrderBy(t => t.Brand).ThenBy(t => t.Model)
            .ToListAsync();

    public Task<Tire?> GetTireAsync(int id, bool includeMovements = false)
    {
        var q = _db.Tires.AsQueryable();
        if (includeMovements)
            q = q.Include(t => t.StockMovements.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id));
        return q.FirstOrDefaultAsync(t => t.Id == id);
    }

    public Task<Tire?> FindByCodeAsync(string code)
        => _db.Tires.FirstOrDefaultAsync(t => t.Sku == code || t.Barcode == code);

    public async Task CreateTireAsync(Tire tire, string? userName = null)
    {
        if (await _db.Tires.AnyAsync(t => t.Sku == tire.Sku))
            throw new DuplicateSkuException(tire.Sku);

        _db.Tires.Add(tire);
        if (tire.Quantity > 0)
        {
            tire.StockMovements.Add(new StockMovement
            {
                MovementType = MovementType.Adjustment,
                Quantity = tire.Quantity,
                Date = DateTime.UtcNow,
                UserName = userName
            });
        }
        await _db.SaveChangesAsync();
        _logger.LogInformation("Tire {Sku} created with opening quantity {Quantity} by {User}",
            tire.Sku, tire.Quantity, userName ?? "unknown");
    }

    public async Task UpdateTireAsync(Tire tire)
    {
        var existing = await _db.Tires.FindAsync(tire.Id)
            ?? throw new TireNotFoundException();

        if (await _db.Tires.AnyAsync(t => t.Sku == tire.Sku && t.Id != tire.Id))
            throw new DuplicateSkuException(tire.Sku);

        existing.Sku = tire.Sku;
        existing.Barcode = tire.Barcode;
        existing.Brand = tire.Brand;
        existing.Model = tire.Model;
        existing.Width = tire.Width;
        existing.Profile = tire.Profile;
        existing.Diameter = tire.Diameter;
        existing.Season = tire.Season;
        existing.Type = tire.Type;
        existing.UnitPrice = tire.UnitPrice;
        existing.MinStock = tire.MinStock;
        existing.Location = tire.Location;
        // Quantity deliberately not copied: stock changes only via movements.

        var entry = _db.Entry(existing);
        entry.Property(t => t.Version).OriginalValue = tire.Version;
        existing.Version++;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new StaleTireException();
        }
    }

    public async Task DeleteTireAsync(int id)
    {
        var tire = await _db.Tires.FindAsync(id);
        if (tire is null) return;

        if (await _db.StockMovements.AnyAsync(m => m.TireId == id))
            throw new TireHasMovementsException();

        _db.Tires.Remove(tire);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Tire {Sku} (id {Id}) deleted", tire.Sku, id);
    }

    public async Task<WarehouseStats> GetStatsAsync()
    {
        var totalSkus = await _db.Tires.CountAsync();
        if (totalSkus == 0)
            return new WarehouseStats(0, 0, 0, 0m);

        var totalUnits = await _db.Tires.SumAsync(t => t.Quantity);
        var lowStock = await _db.Tires.CountAsync(t => t.Quantity <= t.MinStock);
        var totalValue = await _db.Tires.SumAsync(t => (decimal)t.Quantity * t.UnitPrice);
        return new WarehouseStats(totalSkus, totalUnits, lowStock, totalValue);
    }

    public async Task RegisterMovementAsync(int tireId, MovementType movementType, int quantity, string? note, string? userName = null)
    {
        if (movementType != MovementType.Adjustment && quantity < 1)
            throw new InvalidMovementQuantityException();
        if (quantity < 0)
            throw new InvalidMovementQuantityException();

        for (var attempt = 1; ; attempt++)
        {
            var tire = await _db.Tires.FindAsync(tireId)
                ?? throw new TireNotFoundException();

            tire.Quantity = movementType switch
            {
                MovementType.In => tire.Quantity + quantity,
                MovementType.Out when tire.Quantity >= quantity => tire.Quantity - quantity,
                MovementType.Out => throw new InsufficientStockException(tire.Quantity, quantity),
                MovementType.Adjustment => quantity,
                _ => throw new ArgumentOutOfRangeException(nameof(movementType))
            };
            tire.Version++;

            var movement = new StockMovement
            {
                TireId = tireId,
                MovementType = movementType,
                Quantity = quantity,
                Note = note,
                UserName = userName,
                Date = DateTime.UtcNow
            };
            _db.StockMovements.Add(movement);

            try
            {
                await _db.SaveChangesAsync();
                _logger.LogInformation("Movement {Type} x{Quantity} on tire {TireId} by {User}; stock now {Stock}",
                    movementType, quantity, tireId, userName ?? "unknown", tire.Quantity);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < ConcurrencyRetries)
            {
                _db.StockMovements.Remove(movement);
                await _db.Entry(tire).ReloadAsync();
            }
        }
    }

    public async Task<PagedResult<StockMovement>> GetMovementsAsync(MovementType? type, int page, int pageSize = DefaultPageSize)
    {
        var q = _db.StockMovements.Include(m => m.Tire).AsQueryable();
        if (type.HasValue)
            q = q.Where(m => m.MovementType == type);
        q = q.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id);

        page = Math.Max(1, page);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<StockMovement>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ValueReport> GetValueReportAsync()
    {
        var byBrand = await _db.Tires
            .GroupBy(t => t.Brand)
            .Select(g => new
            {
                Key = g.Key,
                Skus = g.Count(),
                Units = g.Sum(t => t.Quantity),
                Value = g.Sum(t => (decimal)t.Quantity * t.UnitPrice)
            })
            .ToListAsync();

        var bySeason = await _db.Tires
            .GroupBy(t => t.Season)
            .Select(g => new
            {
                Key = g.Key,
                Skus = g.Count(),
                Units = g.Sum(t => t.Quantity),
                Value = g.Sum(t => (decimal)t.Quantity * t.UnitPrice)
            })
            .ToListAsync();

        return new ValueReport(
            byBrand.OrderByDescending(g => g.Value)
                .Select(g => new ValueReportGroup(g.Key, g.Skus, g.Units, g.Value)).ToList(),
            bySeason.OrderByDescending(g => g.Value)
                .Select(g => new ValueReportGroup(Helpers.Enums.Key(g.Key), g.Skus, g.Units, g.Value)).ToList(),
            byBrand.Sum(g => g.Value));
    }

    public Task<byte[]> ExportCsvAsync(IEnumerable<Tire> tires)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SKU,Barcode,Brand,Model,Size,Season,Type,UnitPrice,Qty,MinStock,Location");
        foreach (var t in tires)
        {
            sb.AppendLine(
                $"{Csv(t.Sku)},{Csv(t.Barcode ?? "")},{Csv(t.Brand)},{Csv(t.Model)},{Csv(t.Size)}," +
                $"{t.Season},{t.Type},{t.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)},{t.Quantity},{t.MinStock},{Csv(t.Location ?? "")}");
        }
        return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string Csv(string s)
    {
        // Leading '=', '+', '-', '@' would execute as a formula when opened in Excel.
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@')
            s = "'" + s;
        return s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
    }
}
