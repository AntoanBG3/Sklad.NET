using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sklad.Data;
using Sklad.Helpers;
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
        var q = _db.Tires.AsNoTracking()
            .ApplyFilters(filter)
            .ApplySort(filter.Sort);

        var total = await q.CountAsync();
        var page = Pagination.ClampPage(filter.Page, total, pageSize);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<Tire>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<IEnumerable<Tire>> GetLowStockAsync()
        => await _db.Tires.AsNoTracking()
            .Where(t => t.Quantity <= t.MinStock)
            .OrderBy(t => t.Brand).ThenBy(t => t.Model)
            .ToListAsync();

    public const int RecentMovementLimit = 20;

    // Read-only queries stay untracked: every write path (UpdateTireAsync,
    // RegisterMovementAsync, DeleteTireAsync) loads its own tracked entity.
    public Task<Tire?> GetTireAsync(int id, bool includeMovements = false)
    {
        var q = _db.Tires.AsNoTracking();
        if (includeMovements)
            q = q.Include(t => t.StockMovements
                .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id)
                .Take(RecentMovementLimit));
        return q.FirstOrDefaultAsync(t => t.Id == id);
    }

    public Task<int> CountMovementsAsync(int tireId)
        => _db.StockMovements.CountAsync(m => m.TireId == tireId);

    public Task<Tire?> FindByCodeAsync(string code)
        => _db.Tires.AsNoTracking().FirstOrDefaultAsync(t => t.Sku == code || t.Barcode == code);

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
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueSkuViolation(ex))
        {
            throw new DuplicateSkuException(tire.Sku);
        }
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
        catch (DbUpdateException ex) when (IsUniqueSkuViolation(ex))
        {
            throw new DuplicateSkuException(tire.Sku);
        }
    }

    // The AnyAsync pre-checks are advisory; under a concurrent submit the unique
    // index is the real guard and must surface as the same typed exception.
    private static bool IsUniqueSkuViolation(DbUpdateException ex)
        => ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } inner
           && inner.Message.Contains("Sku");

    public async Task DeleteTireAsync(int id)
    {
        var tire = await _db.Tires.FindAsync(id);
        if (tire is null) return;

        if (await _db.StockMovements.AnyAsync(m => m.TireId == id))
            throw new TireHasMovementsException();

        if (await _db.PurchaseOrderItems.AnyAsync(i => i.TireId == id))
            throw new TireOnOrderException();

        if (await _db.StocktakeItems.AnyAsync(item => item.TireId == id))
            throw new TireOnStocktakeException();

        _db.Tires.Remove(tire);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Tire {Sku} (id {Id}) deleted", tire.Sku, id);
    }

    public async Task<int> RegisterMovementAsync(int tireId, MovementType movementType, int quantity, string? note, string? userName = null)
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
                MovementType.In => StockQuantity.Add(tire.Quantity, quantity),
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
                return tire.Quantity;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (attempt >= ConcurrencyRetries)
                {
                    _logger.LogWarning("Movement on tire {TireId} abandoned after {Attempts} version conflicts",
                        tireId, attempt);
                    throw new StaleTireException();
                }
                // A failed SaveChanges leaves database-assigned keys and stale
                // entries in the tracker; detaching by hand corrupts the second
                // retry. Start each attempt from a clean tracker instead.
                _db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<PagedResult<StockMovement>> GetMovementsAsync(MovementType? type, int? tireId = null, DateOnly? from = null, DateOnly? to = null, int page = 1, int pageSize = DefaultPageSize)
    {
        var q = _db.StockMovements.AsNoTracking().Include(m => m.Tire).AsQueryable();
        if (type.HasValue)
            q = q.Where(m => m.MovementType == type);
        if (tireId.HasValue)
            q = q.Where(m => m.TireId == tireId);
        if (from.HasValue)
        {
            var fromUtc = Dates.StartOfDayUtc(from.Value);
            q = q.Where(m => m.Date >= fromUtc);
        }
        if (to.HasValue)
        {
            var toUtc = Dates.StartOfDayUtc(to.Value.AddDays(1));
            q = q.Where(m => m.Date < toUtc);
        }
        q = q.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id);

        var total = await q.CountAsync();
        page = Pagination.ClampPage(page, total, pageSize);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<StockMovement>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<FilterOptions> GetFilterOptionsAsync()
    {
        var brands = await _db.Tires.Select(t => t.Brand).Distinct().OrderBy(b => b).ToListAsync();
        var widths = await _db.Tires.Select(t => t.Width).Distinct().OrderBy(w => w).ToListAsync();
        var profiles = await _db.Tires.Select(t => t.Profile).Distinct().OrderBy(p => p).ToListAsync();
        var diameters = await _db.Tires.Select(t => t.Diameter).Distinct().OrderBy(d => d).ToListAsync();
        var locations = await _db.Tires.Where(t => t.Location != null)
            .Select(t => t.Location!).Distinct().OrderBy(l => l).ToListAsync();
        return new FilterOptions(brands, widths, profiles, diameters, locations);
    }

}
