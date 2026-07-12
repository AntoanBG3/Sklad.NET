using Sklad.Data;
using Sklad.Models;
using Sklad.ViewModels;

namespace Sklad.Services;

/// <summary>
/// Keeps the inventory's filter/sort vocabulary in one place so paged screens
/// and full exports cannot quietly drift into different query semantics.
/// </summary>
internal static class TireQuery
{
    public static IQueryable<Tire> ApplyFilters(
        this IQueryable<Tire> query,
        TireFilterViewModel filter)
    {
        // SQLite's own case folding is ASCII-only. unilower() is registered on
        // each connection and handles both Latin and Cyrillic text.
        if (!string.IsNullOrWhiteSpace(filter.Sku))
        {
            var sku = filter.Sku.Trim().ToLowerInvariant();
            query = query.Where(t => SkladDbContext.UniLower(t.Sku).Contains(sku));
        }

        if (!string.IsNullOrWhiteSpace(filter.Barcode))
            query = query.Where(t => t.Barcode == filter.Barcode);

        if (!string.IsNullOrWhiteSpace(filter.Brand))
        {
            var brand = filter.Brand.Trim().ToLowerInvariant();
            query = query.Where(t => SkladDbContext.UniLower(t.Brand).Contains(brand));
        }

        if (!string.IsNullOrWhiteSpace(filter.Model))
        {
            var model = filter.Model.Trim().ToLowerInvariant();
            query = query.Where(t => SkladDbContext.UniLower(t.Model).Contains(model));
        }

        if (filter.Width.HasValue)
            query = query.Where(t => t.Width == filter.Width);
        if (filter.Profile.HasValue)
            query = query.Where(t => t.Profile == filter.Profile);
        if (filter.Diameter.HasValue)
            query = query.Where(t => t.Diameter == filter.Diameter);
        if (filter.Season.HasValue)
            query = query.Where(t => t.Season == filter.Season);
        if (filter.Type.HasValue)
            query = query.Where(t => t.Type == filter.Type);
        if (!string.IsNullOrWhiteSpace(filter.Location))
            query = query.Where(t => t.Location == filter.Location);
        if (filter.LowOnly == true)
            query = query.Where(t => t.Quantity <= t.MinStock);

        return query;
    }

    public static IQueryable<Tire> ApplySort(this IQueryable<Tire> query, string? sort) => sort switch
    {
        "sku" => query.OrderBy(t => t.Sku),
        "-sku" => query.OrderByDescending(t => t.Sku),
        // SQLite stores decimals as text. Casting keeps numeric ordering stable
        // under cultures that use a comma decimal separator.
        "price" => query.OrderBy(t => (double)t.UnitPrice),
        "-price" => query.OrderByDescending(t => (double)t.UnitPrice),
        "qty" => query.OrderBy(t => t.Quantity),
        "-qty" => query.OrderByDescending(t => t.Quantity),
        "size" => query.OrderBy(t => t.Width).ThenBy(t => t.Profile).ThenBy(t => t.Diameter),
        "-brand" => query.OrderByDescending(t => t.Brand).ThenByDescending(t => t.Model),
        _ => query.OrderBy(t => t.Brand).ThenBy(t => t.Model)
    };
}
