using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Tests;

public class InventoryServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    private InventoryService CreateService(Data.SkladDbContext context) =>
        new(context, NullLogger<InventoryService>.Instance);

    private static Tire NewTire(string sku, string brand = "Michelin", string model = "Primacy 4",
        int width = 205, int profile = 55, int diameter = 16,
        Season season = Season.Summer, TireType type = TireType.New,
        decimal price = 100m, int qty = 10, int minStock = 5, string? barcode = null, string? location = null) =>
        new()
        {
            Sku = sku, Brand = brand, Model = model,
            Width = width, Profile = profile, Diameter = diameter,
            Season = season, Type = type,
            UnitPrice = price, Quantity = qty, MinStock = minStock, Barcode = barcode, Location = location
        };

    private async Task<Tire> SeedTireAsync(Tire tire)
    {
        await using var context = _db.CreateContext();
        context.Tires.Add(tire);
        await context.SaveChangesAsync();
        return tire;
    }

    public void Dispose() => _db.Dispose();

    // --- CreateTireAsync ---

    [Fact]
    public async Task Create_rejects_duplicate_sku()
    {
        await SeedTireAsync(NewTire("DUP-1"));
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        var ex = await Assert.ThrowsAsync<DuplicateSkuException>(
            () => service.CreateTireAsync(NewTire("DUP-1", brand: "Other")));
        Assert.Equal("DUP-1", ex.Sku);
    }

    [Fact]
    public async Task Create_with_opening_quantity_records_initial_adjustment_movement()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        await service.CreateTireAsync(NewTire("NEW-1", qty: 12), userName: "ivan");

        var movement = Assert.Single(context.StockMovements);
        Assert.Equal(MovementType.Adjustment, movement.MovementType);
        Assert.Equal(12, movement.Quantity);
        Assert.Equal("ivan", movement.UserName);
    }

    [Fact]
    public async Task Create_with_zero_quantity_records_no_movement()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        await service.CreateTireAsync(NewTire("NEW-2", qty: 0));

        Assert.Empty(context.StockMovements);
    }

    // --- UpdateTireAsync ---

    [Fact]
    public async Task Update_preserves_barcode_when_posted_value_carries_it()
    {
        var seeded = await SeedTireAsync(NewTire("UPD-1", barcode: "3528706782900"));
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        var posted = NewTire("UPD-1", brand: "Michelin", model: "Renamed", barcode: "3528706782900");
        posted.Id = seeded.Id;
        posted.Version = seeded.Version;
        await service.UpdateTireAsync(posted);

        await using var verify = _db.CreateContext();
        var stored = await verify.Tires.FindAsync(seeded.Id);
        Assert.Equal("3528706782900", stored!.Barcode);
        Assert.Equal("Renamed", stored.Model);
    }

    [Fact]
    public async Task Update_does_not_change_quantity()
    {
        var seeded = await SeedTireAsync(NewTire("UPD-2", qty: 10));
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        var posted = NewTire("UPD-2", qty: 999);
        posted.Id = seeded.Id;
        posted.Version = seeded.Version;
        await service.UpdateTireAsync(posted);

        await using var verify = _db.CreateContext();
        Assert.Equal(10, (await verify.Tires.FindAsync(seeded.Id))!.Quantity);
    }

    [Fact]
    public async Task Update_rejects_sku_already_used_by_another_tire()
    {
        await SeedTireAsync(NewTire("TAKEN"));
        var seeded = await SeedTireAsync(NewTire("MINE"));
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        var posted = NewTire("TAKEN");
        posted.Id = seeded.Id;
        posted.Version = seeded.Version;
        await Assert.ThrowsAsync<DuplicateSkuException>(() => service.UpdateTireAsync(posted));
    }

    [Fact]
    public async Task Update_with_stale_version_throws()
    {
        var seeded = await SeedTireAsync(NewTire("STALE-1"));
        var staleVersion = seeded.Version;

        await using (var context = _db.CreateContext())
        {
            await CreateService(context).RegisterMovementAsync(seeded.Id, MovementType.In, 5, null);
        }

        await using var context2 = _db.CreateContext();
        var posted = NewTire("STALE-1", brand: "Changed");
        posted.Id = seeded.Id;
        posted.Version = staleVersion;
        await Assert.ThrowsAsync<StaleTireException>(() => CreateService(context2).UpdateTireAsync(posted));
    }

    [Fact]
    public async Task Update_missing_tire_throws_not_found()
    {
        await using var context = _db.CreateContext();
        var posted = NewTire("GHOST");
        posted.Id = 12345;
        await Assert.ThrowsAsync<TireNotFoundException>(() => CreateService(context).UpdateTireAsync(posted));
    }

    // --- DeleteTireAsync ---

    [Fact]
    public async Task Delete_removes_tire_without_movements()
    {
        var seeded = await SeedTireAsync(NewTire("DEL-1", qty: 0));
        await using var context = _db.CreateContext();

        await CreateService(context).DeleteTireAsync(seeded.Id);

        await using var verify = _db.CreateContext();
        Assert.Empty(verify.Tires);
    }

    [Fact]
    public async Task Delete_with_movement_history_throws()
    {
        var seeded = await SeedTireAsync(NewTire("DEL-2"));
        await using (var context = _db.CreateContext())
        {
            await CreateService(context).RegisterMovementAsync(seeded.Id, MovementType.In, 1, null);
        }

        await using var context2 = _db.CreateContext();
        await Assert.ThrowsAsync<TireHasMovementsException>(
            () => CreateService(context2).DeleteTireAsync(seeded.Id));
    }

    // --- RegisterMovementAsync ---

    [Fact]
    public async Task In_adds_to_quantity_and_records_movement_with_user()
    {
        var seeded = await SeedTireAsync(NewTire("MOV-1", qty: 10));
        await using var context = _db.CreateContext();

        await CreateService(context).RegisterMovementAsync(seeded.Id, MovementType.In, 5, "delivery", "maria");

        await using var verify = _db.CreateContext();
        Assert.Equal(15, (await verify.Tires.FindAsync(seeded.Id))!.Quantity);
        var movement = Assert.Single(verify.StockMovements);
        Assert.Equal("maria", movement.UserName);
        Assert.Equal("delivery", movement.Note);
    }

    [Fact]
    public async Task Out_subtracts_from_quantity()
    {
        var seeded = await SeedTireAsync(NewTire("MOV-2", qty: 10));
        await using var context = _db.CreateContext();

        await CreateService(context).RegisterMovementAsync(seeded.Id, MovementType.Out, 4, null);

        await using var verify = _db.CreateContext();
        Assert.Equal(6, (await verify.Tires.FindAsync(seeded.Id))!.Quantity);
    }

    [Fact]
    public async Task Out_exceeding_stock_throws_with_amounts()
    {
        var seeded = await SeedTireAsync(NewTire("MOV-3", qty: 3));
        await using var context = _db.CreateContext();

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => CreateService(context).RegisterMovementAsync(seeded.Id, MovementType.Out, 5, null));
        Assert.Equal(3, ex.Available);
        Assert.Equal(5, ex.Requested);

        await using var verify = _db.CreateContext();
        Assert.Equal(3, (await verify.Tires.FindAsync(seeded.Id))!.Quantity);
        Assert.Empty(verify.StockMovements);
    }

    [Theory]
    [InlineData(MovementType.In, 0)]
    [InlineData(MovementType.Out, 0)]
    [InlineData(MovementType.Adjustment, -1)]
    public async Task Invalid_quantities_throw(MovementType type, int quantity)
    {
        var seeded = await SeedTireAsync(NewTire("MOV-4", qty: 10));
        await using var context = _db.CreateContext();

        await Assert.ThrowsAsync<InvalidMovementQuantityException>(
            () => CreateService(context).RegisterMovementAsync(seeded.Id, type, quantity, null));
    }

    [Fact]
    public async Task Adjustment_to_zero_writes_off_stock_and_passes_entity_validation()
    {
        var seeded = await SeedTireAsync(NewTire("MOV-5", qty: 10));
        await using var context = _db.CreateContext();

        await CreateService(context).RegisterMovementAsync(seeded.Id, MovementType.Adjustment, 0, "write-off");

        await using var verify = _db.CreateContext();
        Assert.Equal(0, (await verify.Tires.FindAsync(seeded.Id))!.Quantity);
        var movement = Assert.Single(verify.StockMovements);
        var results = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(movement, new ValidationContext(movement), results, true));
    }

    [Fact]
    public async Task Movement_on_missing_tire_throws_not_found()
    {
        await using var context = _db.CreateContext();
        await Assert.ThrowsAsync<TireNotFoundException>(
            () => CreateService(context).RegisterMovementAsync(999, MovementType.In, 1, null));
    }

    [Fact]
    public async Task Movement_bumps_the_concurrency_version()
    {
        var seeded = await SeedTireAsync(NewTire("MOV-6", qty: 10));
        var before = seeded.Version;
        await using var context = _db.CreateContext();

        await CreateService(context).RegisterMovementAsync(seeded.Id, MovementType.In, 1, null);

        await using var verify = _db.CreateContext();
        Assert.True((await verify.Tires.FindAsync(seeded.Id))!.Version > before);
    }

    // --- SearchAsync: filtering, sorting, paging ---

    private async Task SeedSearchSetAsync()
    {
        await SeedTireAsync(NewTire("A-1", brand: "Michelin", model: "Primacy", price: 100, qty: 10, width: 205, season: Season.Summer, barcode: "111"));
        await SeedTireAsync(NewTire("B-2", brand: "Pirelli", model: "P Zero", price: 300, qty: 2, width: 225, season: Season.Winter, barcode: "222"));
        await SeedTireAsync(NewTire("C-3", brand: "Hankook", model: "Kinergy", price: 50, qty: 60, width: 205, season: Season.Summer));
    }

    [Fact]
    public async Task Search_without_filters_returns_everything()
    {
        await SeedSearchSetAsync();
        await using var context = _db.CreateContext();

        var result = await CreateService(context).SearchAsync(new TireFilterViewModel());

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task Search_filters_by_barcode_exactly()
    {
        await SeedSearchSetAsync();
        await using var context = _db.CreateContext();

        var result = await CreateService(context).SearchAsync(new TireFilterViewModel { Barcode = "222" });

        var tire = Assert.Single(result.Items);
        Assert.Equal("B-2", tire.Sku);
    }

    [Fact]
    public async Task Search_combines_width_and_season()
    {
        await SeedSearchSetAsync();
        await using var context = _db.CreateContext();

        var result = await CreateService(context).SearchAsync(
            new TireFilterViewModel { Width = 205, Season = Season.Summer });

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Search_sorts_by_price_descending()
    {
        await SeedSearchSetAsync();
        await using var context = _db.CreateContext();

        var result = await CreateService(context).SearchAsync(new TireFilterViewModel { Sort = "-price" });

        Assert.Equal(new[] { "B-2", "A-1", "C-3" }, result.Items.Select(t => t.Sku).ToArray());
    }

    [Fact]
    public async Task Search_pages_results()
    {
        await SeedSearchSetAsync();
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        var page1 = await service.SearchAsync(new TireFilterViewModel { Sort = "sku", Page = 1 }, pageSize: 2);
        var page2 = await service.SearchAsync(new TireFilterViewModel { Sort = "sku", Page = 2 }, pageSize: 2);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(new[] { "A-1", "B-2" }, page1.Items.Select(t => t.Sku).ToArray());
        var last = Assert.Single(page2.Items);
        Assert.Equal("C-3", last.Sku);
    }

    // --- FindByCodeAsync ---

    [Fact]
    public async Task FindByCode_matches_sku_or_barcode_exactly()
    {
        await SeedSearchSetAsync();
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        Assert.Equal("A-1", (await service.FindByCodeAsync("A-1"))?.Sku);
        Assert.Equal("B-2", (await service.FindByCodeAsync("222"))?.Sku);
        Assert.Null(await service.FindByCodeAsync("A"));
    }

    // --- GetLowStockAsync ---

    [Fact]
    public async Task LowStock_returns_only_tires_at_or_below_minimum()
    {
        await SeedTireAsync(NewTire("LOW-1", qty: 2, minStock: 5));
        await SeedTireAsync(NewTire("LOW-2", qty: 5, minStock: 5));
        await SeedTireAsync(NewTire("OK-1", qty: 9, minStock: 5));
        await using var context = _db.CreateContext();

        var result = (await CreateService(context).GetLowStockAsync()).Select(t => t.Sku).ToArray();

        Assert.Equal(2, result.Length);
        Assert.DoesNotContain("OK-1", result);
    }

    // --- GetStatsAsync ---

    [Fact]
    public async Task Stats_aggregate_counts_units_lowstock_and_value()
    {
        await SeedTireAsync(NewTire("S-1", price: 10m, qty: 3, minStock: 5));
        await SeedTireAsync(NewTire("S-2", price: 100m, qty: 2, minStock: 1));
        await using var context = _db.CreateContext();

        var stats = await CreateService(context).GetStatsAsync();

        Assert.Equal(2, stats.TotalSkus);
        Assert.Equal(5, stats.TotalUnits);
        Assert.Equal(1, stats.LowStockCount);
        Assert.Equal(230m, stats.TotalValue);
    }

    [Fact]
    public async Task Stats_on_empty_database_are_zero()
    {
        await using var context = _db.CreateContext();
        var stats = await CreateService(context).GetStatsAsync();
        Assert.Equal(0, stats.TotalSkus);
        Assert.Equal(0m, stats.TotalValue);
    }

    // --- GetMovementsAsync (journal) ---

    [Fact]
    public async Task Movements_journal_filters_by_type_newest_first_with_tire()
    {
        var seeded = await SeedTireAsync(NewTire("J-1", qty: 10));
        await using (var context = _db.CreateContext())
        {
            var service = CreateService(context);
            await service.RegisterMovementAsync(seeded.Id, MovementType.In, 5, "first");
            await service.RegisterMovementAsync(seeded.Id, MovementType.Out, 2, "second");
            await service.RegisterMovementAsync(seeded.Id, MovementType.In, 1, "third");
        }

        await using var context2 = _db.CreateContext();
        var all = await CreateService(context2).GetMovementsAsync(null, page: 1);
        var ins = await CreateService(context2).GetMovementsAsync(MovementType.In, page: 1);

        Assert.Equal(3, all.TotalCount);
        Assert.Equal("third", all.Items[0].Note);
        Assert.All(all.Items, m => Assert.NotNull(m.Tire));
        Assert.Equal(2, ins.TotalCount);
    }

    // --- GetValueReportAsync ---

    [Fact]
    public async Task Value_report_groups_by_brand_and_season()
    {
        await SeedTireAsync(NewTire("V-1", brand: "Michelin", price: 10m, qty: 2, season: Season.Summer));
        await SeedTireAsync(NewTire("V-2", brand: "Michelin", price: 5m, qty: 4, season: Season.Winter));
        await SeedTireAsync(NewTire("V-3", brand: "Pirelli", price: 100m, qty: 1, season: Season.Summer));
        await using var context = _db.CreateContext();

        var report = await CreateService(context).GetValueReportAsync();

        Assert.Equal(140m, report.TotalValue);
        var pirelli = report.ByBrand.Single(g => g.Key == "Pirelli");
        Assert.Equal(1, pirelli.Skus);
        Assert.Equal(100m, pirelli.Value);
        var michelin = report.ByBrand.Single(g => g.Key == "Michelin");
        Assert.Equal(2, michelin.Skus);
        Assert.Equal(6, michelin.Units);
        Assert.Equal(40m, michelin.Value);
        var summer = report.BySeason.Single(g => g.Key == "Summer");
        Assert.Equal(120m, summer.Value);
    }

    // --- ExportCsvAsync ---

    [Fact]
    public async Task Export_uses_invariant_decimals_and_escapes_quotes_and_commas()
    {
        var tire = NewTire("CSV-1", brand: "Brand, Inc", model: "Say \"hi\"", price: 1234.5m, qty: 7);
        await using var context = _db.CreateContext();

        var bytes = await CreateService(context).ExportCsvAsync(new[] { tire });
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("1234.50", text);
        Assert.Contains("\"Brand, Inc\"", text);
        Assert.Contains("\"Say \"\"hi\"\"\"", text);
    }

    [Fact]
    public async Task Export_neutralizes_spreadsheet_formula_injection()
    {
        var tire = NewTire("=CSV-2", brand: "+SUM(A1)", model: "-2+3", location: "@cmd");
        await using var context = _db.CreateContext();

        var bytes = await CreateService(context).ExportCsvAsync(new[] { tire });
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("'=CSV-2", text);
        Assert.Contains("'+SUM(A1)", text);
        Assert.Contains("'-2+3", text);
        Assert.Contains("'@cmd", text);
    }

    [Fact]
    public async Task Export_starts_with_utf8_bom_so_excel_reads_cyrillic()
    {
        await using var context = _db.CreateContext();

        var bytes = await CreateService(context).ExportCsvAsync(new[] { NewTire("BOM-1", location: "Рафт А-3") });

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.Contains("Рафт А-3", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Export_quotes_fields_containing_carriage_returns()
    {
        await using var context = _db.CreateContext();

        var bytes = await CreateService(context).ExportCsvAsync(new[] { NewTire("CR-1", location: "A\rB") });
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("\"A\rB\"", text);
    }

    // --- Case sensitivity ---

    [Fact]
    public async Task FindByCode_ignores_case_for_sku_and_barcode()
    {
        await SeedTireAsync(NewTire("MIC-ABC", barcode: "CODE99X"));
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        Assert.NotNull(await service.FindByCodeAsync("mic-abc"));
        Assert.NotNull(await service.FindByCodeAsync("code99x"));
    }

    [Fact]
    public async Task Duplicate_sku_detection_ignores_case()
    {
        await SeedTireAsync(NewTire("CASE-1"));
        await using var context = _db.CreateContext();

        await Assert.ThrowsAsync<DuplicateSkuException>(
            () => CreateService(context).CreateTireAsync(NewTire("case-1")));
    }

    [Fact]
    public async Task Search_text_filters_ignore_case_including_cyrillic()
    {
        await SeedTireAsync(NewTire("CYR-1", brand: "Гума Про"));
        await SeedTireAsync(NewTire("LAT-1", brand: "Michelin"));
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        var cyrillic = await service.SearchAsync(new TireFilterViewModel { Brand = "гума" });
        Assert.Equal("CYR-1", Assert.Single(cyrillic.Items).Sku);

        var latin = await service.SearchAsync(new TireFilterViewModel { Brand = "michelin" });
        Assert.Equal("LAT-1", Assert.Single(latin.Items).Sku);

        var bySku = await service.SearchAsync(new TireFilterViewModel { Sku = "cyr" });
        Assert.Equal("CYR-1", Assert.Single(bySku.Items).Sku);
    }

    // --- Page clamping ---

    [Fact]
    public async Task Search_clamps_out_of_range_pages_to_the_last_page()
    {
        for (var i = 0; i < 3; i++)
            await SeedTireAsync(NewTire($"PG-{i}"));
        await using var context = _db.CreateContext();

        var result = await CreateService(context).SearchAsync(
            new TireFilterViewModel { Page = 999 }, pageSize: 2);

        Assert.Equal(2, result.Page);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task Movements_journal_clamps_out_of_range_pages()
    {
        var tire = await SeedTireAsync(NewTire("PGM-1"));
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        await service.RegisterMovementAsync(tire.Id, MovementType.In, 1, null);

        var result = await service.GetMovementsAsync(null, page: 999, pageSize: 10);

        Assert.Equal(1, result.Page);
        Assert.Single(result.Items);
    }

    // --- Races ---

    [Fact]
    public async Task Movement_conflicts_exhausting_retries_throw_stale_not_raw_concurrency()
    {
        var tire = await SeedTireAsync(NewTire("RACE-1", qty: 10));
        await using var context = _db.CreateContext(new BumpVersionsOnSave(_db.Connection));
        var service = CreateService(context);

        await Assert.ThrowsAsync<StaleTireException>(
            () => service.RegisterMovementAsync(tire.Id, MovementType.In, 1, null));

        await using var check = _db.CreateContext();
        Assert.Equal(0, await check.StockMovements.CountAsync(m => m.TireId == tire.Id));
    }

    [Fact]
    public async Task Losing_a_duplicate_sku_race_throws_typed_exception_not_DbUpdateException()
    {
        await using var context = _db.CreateContext(new InsertDuplicateSkuOnSave(_db.Connection, "RACE-DUP"));

        await Assert.ThrowsAsync<DuplicateSkuException>(
            () => CreateService(context).CreateTireAsync(NewTire("RACE-DUP")));
    }
}
