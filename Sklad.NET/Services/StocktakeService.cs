using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sklad.Data;
using Sklad.Models;

namespace Sklad.Services;

public sealed class StocktakeService(SkladDbContext db, ILogger<StocktakeService> logger)
    : IStocktakeService
{
    public async Task<PagedResult<Stocktake>> GetStocktakesAsync(
        StocktakeStatus? status = null,
        int page = 1,
        int pageSize = InventoryService.DefaultPageSize)
    {
        var query = db.Stocktakes.AsNoTracking().Include(stocktake => stocktake.Items).AsQueryable();
        if (status.HasValue)
            query = query.Where(stocktake => stocktake.Status == status);
        query = query.OrderByDescending(stocktake => stocktake.CreatedAt).ThenByDescending(stocktake => stocktake.Id);

        var total = await query.CountAsync();
        page = Pagination.ClampPage(page, total, pageSize);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<Stocktake>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public Task<Stocktake?> GetStocktakeAsync(int id)
        => db.Stocktakes.AsNoTracking()
            .Include(stocktake => stocktake.Items)
            .ThenInclude(item => item.Tire)
            .FirstOrDefaultAsync(stocktake => stocktake.Id == id);

    public async Task<Stocktake> CreateAsync(string? location, string? note, string? userName = null)
    {
        location = Clean(location);
        note = Clean(note);

        var query = db.Tires.AsNoTracking().AsQueryable();
        if (location is not null)
            query = query.Where(tire => tire.Location == location);

        var tires = await query.OrderBy(tire => tire.Location).ThenBy(tire => tire.Brand)
            .ThenBy(tire => tire.Model).ThenBy(tire => tire.Sku).ToListAsync();
        if (tires.Count == 0)
            throw new EmptyStocktakeException();

        var tireIds = tires.Select(tire => tire.Id).ToList();
        var active = await db.Stocktakes.AsNoTracking()
            .Where(stocktake => stocktake.Status == StocktakeStatus.Draft)
            .Where(stocktake => stocktake.Items.Any(item => tireIds.Contains(item.TireId)))
            .OrderBy(stocktake => stocktake.Id)
            .FirstOrDefaultAsync();
        if (active is not null)
            throw new ActiveStocktakeExistsException(active.Number);

        var stocktake = new Stocktake
        {
            Status = StocktakeStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            Location = location,
            Note = note,
            CreatedBy = userName
        };
        foreach (var tire in tires)
        {
            stocktake.Items.Add(new StocktakeItem
            {
                TireId = tire.Id,
                ExpectedQuantity = tire.Quantity,
                ExpectedTireVersion = tire.Version
            });
        }

        db.Stocktakes.Add(stocktake);
        await db.SaveChangesAsync();
        logger.LogInformation("Stocktake {Number} created for {Count} tires at {Location} by {User}",
            stocktake.Number, tires.Count, location ?? "all locations", userName ?? "unknown");
        return stocktake;
    }

    public async Task SaveCountsAsync(int id, int expectedVersion, IReadOnlyList<StocktakeCount> counts)
    {
        var stocktake = await db.Stocktakes.Include(entry => entry.Items)
            .FirstOrDefaultAsync(entry => entry.Id == id)
            ?? throw new StocktakeNotFoundException();
        EnsureOpenAndExpected(stocktake, expectedVersion);

        if (counts.Select(count => count.ItemId).Distinct().Count() != counts.Count ||
            counts.Any(count => count.CountedQuantity < 0 || Clean(count.Note)?.Length > 500))
            throw new InvalidStocktakeLinesException();

        var byId = stocktake.Items.ToDictionary(item => item.Id);
        if (counts.Any(count => !byId.ContainsKey(count.ItemId)))
            throw new InvalidStocktakeLinesException();

        foreach (var count in counts)
        {
            var item = byId[count.ItemId];
            item.CountedQuantity = count.CountedQuantity;
            item.Note = Clean(count.Note);
        }

        await SaveMutationAsync(stocktake);
        logger.LogInformation("Counts saved on stocktake {Number} ({Counted}/{Total})",
            stocktake.Number, stocktake.CountedItems, stocktake.Items.Count);
    }

    public async Task<int> CompleteAsync(int id, int expectedVersion, string? userName = null)
    {
        var stocktake = await db.Stocktakes.Include(entry => entry.Items)
            .FirstOrDefaultAsync(entry => entry.Id == id)
            ?? throw new StocktakeNotFoundException();
        EnsureOpenAndExpected(stocktake, expectedVersion);

        var remaining = stocktake.Items.Count(item => !item.CountedQuantity.HasValue);
        if (remaining > 0)
            throw new IncompleteStocktakeException(remaining);

        var tireIds = stocktake.Items.Select(item => item.TireId).ToList();
        var tires = await db.Tires.Where(tire => tireIds.Contains(tire.Id)).ToDictionaryAsync(tire => tire.Id);
        if (tires.Count != tireIds.Count)
            throw new TireNotFoundException();

        var changedSkus = stocktake.Items
            .Where(item => tires[item.TireId].Version != item.ExpectedTireVersion)
            .Select(item => tires[item.TireId].Sku)
            .OrderBy(sku => sku)
            .ToList();
        if (changedSkus.Count > 0)
            throw new StocktakeInventoryChangedException(changedSkus);

        var now = DateTime.UtcNow;
        foreach (var item in stocktake.Items.Where(item => item.CountedQuantity != item.ExpectedQuantity))
        {
            var tire = tires[item.TireId];
            tire.Quantity = item.CountedQuantity!.Value;
            tire.Version++;
            db.StockMovements.Add(new StockMovement
            {
                TireId = tire.Id,
                MovementType = MovementType.Adjustment,
                Quantity = item.CountedQuantity.Value,
                Date = now,
                UserName = userName,
                Note = MovementNote(stocktake.Number, item.Note)
            });
        }

        stocktake.Status = StocktakeStatus.Completed;
        stocktake.CompletedAt = now;
        stocktake.CompletedBy = userName;
        stocktake.Version++;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(entry => entry.Entity is Stocktake))
        {
            throw new StaleStocktakeException();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new StocktakeInventoryChangedException([]);
        }

        logger.LogInformation("Stocktake {Number} completed with {Variances} variances by {User}",
            stocktake.Number, stocktake.VarianceItems, userName ?? "unknown");
        return stocktake.VarianceItems;
    }

    public async Task<int> RefreshChangedAsync(int id, int expectedVersion)
    {
        var stocktake = await db.Stocktakes.Include(entry => entry.Items)
            .FirstOrDefaultAsync(entry => entry.Id == id)
            ?? throw new StocktakeNotFoundException();
        EnsureOpenAndExpected(stocktake, expectedVersion);

        var tireIds = stocktake.Items.Select(item => item.TireId).ToList();
        var tires = await db.Tires.AsNoTracking().Where(tire => tireIds.Contains(tire.Id))
            .ToDictionaryAsync(tire => tire.Id);
        if (tires.Count != tireIds.Count)
            throw new TireNotFoundException();

        var changed = 0;
        foreach (var item in stocktake.Items)
        {
            var tire = tires[item.TireId];
            if (tire.Version == item.ExpectedTireVersion)
                continue;

            item.ExpectedQuantity = tire.Quantity;
            item.ExpectedTireVersion = tire.Version;
            item.CountedQuantity = null;
            changed++;
        }

        if (changed > 0)
            await SaveMutationAsync(stocktake);
        return changed;
    }

    public async Task CancelAsync(int id, int expectedVersion)
    {
        var stocktake = await db.Stocktakes.FindAsync(id)
            ?? throw new StocktakeNotFoundException();
        EnsureOpenAndExpected(stocktake, expectedVersion);
        stocktake.Status = StocktakeStatus.Cancelled;
        await SaveMutationAsync(stocktake);
        logger.LogInformation("Stocktake {Number} cancelled", stocktake.Number);
    }

    private static void EnsureOpenAndExpected(Stocktake stocktake, int expectedVersion)
    {
        if (stocktake.Version != expectedVersion)
            throw new StaleStocktakeException();
        if (!stocktake.IsOpen)
            throw new InvalidStocktakeStateException();
    }

    private async Task SaveMutationAsync(Stocktake stocktake)
    {
        stocktake.Version++;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new StaleStocktakeException();
        }
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string MovementNote(string number, string? note)
    {
        var value = string.IsNullOrWhiteSpace(note) ? number : $"{number} — {note.Trim()}";
        return value.Length <= 500 ? value : value[..500];
    }
}
