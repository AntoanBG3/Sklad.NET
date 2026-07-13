using Microsoft.EntityFrameworkCore;
using Sklad.Data;
using Sklad.Helpers;
using Sklad.Models;

namespace Sklad.Services;

public sealed class InventoryReportService : IInventoryReportService
{
    private readonly SkladDbContext _db;

    public InventoryReportService(SkladDbContext db) => _db = db;

    public async Task<WarehouseStats> GetStatsAsync()
    {
        // One aggregate scan instead of four; this runs on every inventory view.
        var stats = await _db.Tires
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Skus = group.Count(),
                Units = group.Sum(tire => (long)tire.Quantity),
                Low = group.Sum(tire => tire.Quantity <= tire.MinStock ? 1 : 0),
                Value = group.Sum(tire => (decimal)tire.Quantity * tire.UnitPrice)
            })
            .SingleOrDefaultAsync();

        return stats is null
            ? new WarehouseStats(0, 0, 0, 0m)
            : new WarehouseStats(stats.Skus, stats.Units, stats.Low, stats.Value);
    }

    public async Task<ValueReport> GetValueReportAsync()
    {
        var byBrand = await _db.Tires
            .GroupBy(tire => tire.Brand)
            .Select(group => new
            {
                Key = group.Key,
                Skus = group.Count(),
                Units = group.Sum(tire => (long)tire.Quantity),
                Value = group.Sum(tire => (decimal)tire.Quantity * tire.UnitPrice)
            })
            .ToListAsync();

        var bySeason = await _db.Tires
            .GroupBy(tire => tire.Season)
            .Select(group => new
            {
                Key = group.Key,
                Skus = group.Count(),
                Units = group.Sum(tire => (long)tire.Quantity),
                Value = group.Sum(tire => (decimal)tire.Quantity * tire.UnitPrice)
            })
            .ToListAsync();

        return new ValueReport(
            byBrand.OrderByDescending(group => group.Value)
                .Select(group => new ValueReportGroup(group.Key, group.Skus, group.Units, group.Value))
                .ToList(),
            bySeason.OrderByDescending(group => group.Value)
                .Select(group => new ValueReportGroup(Enums.Key(group.Key), group.Skus, group.Units, group.Value))
                .ToList(),
            byBrand.Sum(group => group.Value));
    }

    public async Task<MovementTrend> GetMovementTrendAsync(DateOnly from, DateOnly to)
    {
        var fromUtc = Dates.StartOfDayUtc(from);
        var toUtc = Dates.StartOfDayUtc(to.AddDays(1));

        var rows = await _db.StockMovements
            .Where(movement => movement.Date >= fromUtc && movement.Date < toUtc)
            .Select(movement => new { movement.Date, movement.MovementType, movement.Quantity })
            .ToListAsync();

        var granularity = Trend.Granularity(from, to);
        var buckets = new List<TrendBucket>();
        var index = new Dictionary<DateOnly, int>();

        foreach (var start in Trend.Sequence(from, to, granularity))
        {
            index[start] = buckets.Count;
            buckets.Add(new TrendBucket(Trend.Label(start, granularity), 0, 0));
        }

        var adjustments = 0;
        foreach (var row in rows)
        {
            if (row.MovementType == MovementType.Adjustment)
            {
                adjustments++;
                continue;
            }

            var shopDay = DateOnly.FromDateTime(Dates.Shop(row.Date));
            if (!index.TryGetValue(Trend.BucketStart(shopDay, granularity), out var bucketIndex))
                continue;

            buckets[bucketIndex] = row.MovementType == MovementType.In
                ? buckets[bucketIndex] with { In = buckets[bucketIndex].In + row.Quantity }
                : buckets[bucketIndex] with { Out = buckets[bucketIndex].Out + row.Quantity };
        }

        return new MovementTrend(buckets, granularity, adjustments);
    }
}
