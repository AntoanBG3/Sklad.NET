using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Sklad.Controllers;
using Sklad.Data;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Tests;

public class Code128BarcodeServiceTests
{
    private readonly Code128BarcodeService _service = new();

    [Fact]
    public void Encode_builds_the_expected_set_B_checksum_and_geometry()
    {
        var symbol = Assert.IsType<BarcodeSymbol>(_service.Encode("1234"));

        Assert.Equal([104, 17, 18, 19, 20, 88, 106], symbol.Codewords);
        Assert.Equal(99, symbol.ModuleCount);
        Assert.NotEmpty(symbol.Bars);
        Assert.All(symbol.Bars, bar => Assert.True(bar.X >= 10 && bar.Width is >= 1 and <= 4));
        Assert.True(symbol.Bars[^1].X + symbol.Bars[^1].Width <= symbol.ModuleCount - 10);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("line\nbreak")]
    [InlineData("КОД-1")]
    public void Encode_rejects_values_outside_printable_ASCII(string? value)
        => Assert.Null(_service.Encode(value));

    [Fact]
    public void Encode_accepts_the_full_printable_set_B_range()
    {
        var input = new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray());

        var symbol = Assert.IsType<BarcodeSymbol>(_service.Encode(input));

        Assert.Equal(input, symbol.Value);
        Assert.Equal(input.Length + 3, symbol.Codewords.Count);
    }
}

public class BarcodeLabelServiceTests
{
    private readonly BarcodeLabelService _service = new(new Code128BarcodeService());

    [Fact]
    public void Create_prefers_the_configured_barcode()
    {
        var result = _service.Create(NewTire("SKU-1", "3800123456789"));

        Assert.Equal("3800123456789", result.Code);
        Assert.NotNull(result.Barcode);
        Assert.False(result.UsesSkuFallback);
    }

    [Fact]
    public void Create_falls_back_to_the_scannable_SKU()
    {
        var result = _service.Create(NewTire("SKU-1", "КОД-1"));

        Assert.Equal("SKU-1", result.Code);
        Assert.NotNull(result.Barcode);
        Assert.True(result.UsesSkuFallback);
    }

    [Fact]
    public void Create_keeps_human_text_when_neither_identity_can_be_encoded()
    {
        var result = _service.Create(NewTire("ГУМА-1", "КОД-1"));

        Assert.Equal("КОД-1", result.Code);
        Assert.Null(result.Barcode);
    }

    private static Tire NewTire(string sku, string? barcode) => new()
    {
        Sku = sku, Barcode = barcode, Brand = "Test", Model = "M",
        Width = 205, Profile = 55, Diameter = 16, UnitPrice = 100m
    };
}

public class LabelsControllerTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Tire_returns_not_found_for_a_missing_tire()
    {
        await using var context = _db.CreateContext();

        var result = await CreateController(context).Tire(404);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Tire_clamps_copies_and_builds_a_printable_sheet()
    {
        int id;
        await using (var seed = _db.CreateContext())
        {
            var tire = NewTire("ONE-1", "3800123456789");
            seed.Tires.Add(tire);
            await seed.SaveChangesAsync();
            id = tire.Id;
        }

        await using var context = _db.CreateContext();
        var result = Assert.IsType<ViewResult>(await CreateController(context).Tire(id, copies: 999));
        var model = Assert.IsType<LabelSheetViewModel>(result.Model);

        Assert.Equal("Sheet", result.ViewName);
        Assert.Equal(LabelsController.MaxCopies, model.Copies);
        Assert.Equal(LabelsController.MaxCopies, model.Labels.Count);
        Assert.Equal(id, model.SourceTireId);
    }

    [Fact]
    public async Task Index_honours_filters_and_repeats_each_matching_tire()
    {
        await using (var seed = _db.CreateContext())
        {
            seed.Tires.AddRange(
                NewTire("MIC-1", "100", brand: "Michelin"),
                NewTire("MIC-2", "101", brand: "Michelin"),
                NewTire("PIR-1", "102", brand: "Pirelli"));
            await seed.SaveChangesAsync();
        }

        await using var context = _db.CreateContext();
        var result = Assert.IsType<ViewResult>(await CreateController(context).Index(
            new TireFilterViewModel { Brand = "Michelin" }, copies: 3));
        var model = Assert.IsType<LabelSheetViewModel>(result.Model);

        Assert.Equal(2, model.MatchedTires);
        Assert.Equal(6, model.Labels.Count);
        Assert.All(model.Labels, label => Assert.Equal("Michelin", label.Tire.Brand));
        Assert.False(model.IsTruncated);
    }

    [Fact]
    public async Task Index_caps_the_rendered_sheet_before_querying_all_matches()
    {
        await using (var seed = _db.CreateContext())
        {
            for (var i = 0; i < 25; i++)
                seed.Tires.Add(NewTire($"CAP-{i:00}", $"900{i:00}"));
            await seed.SaveChangesAsync();
        }

        await using var context = _db.CreateContext();
        var result = Assert.IsType<ViewResult>(await CreateController(context).Index(
            new TireFilterViewModel(), copies: LabelsController.MaxCopies));
        var model = Assert.IsType<LabelSheetViewModel>(result.Model);

        Assert.Equal(25, model.MatchedTires);
        Assert.Equal(LabelsController.MaxLabels, model.Labels.Count);
        Assert.True(model.IsTruncated);
    }

    private static LabelsController CreateController(SkladDbContext context)
    {
        var inventory = new InventoryService(context, NullLogger<InventoryService>.Instance);
        var labelService = new BarcodeLabelService(new Code128BarcodeService());
        var settings = new ShopSettingsService(
            context, NullLogger<ShopSettingsService>.Instance, new DefaultCultureCache());
        return new LabelsController(inventory, labelService, settings);
    }

    private static Tire NewTire(string sku, string? barcode, string brand = "Test") => new()
    {
        Sku = sku, Barcode = barcode, Brand = brand, Model = "M", Width = 205,
        Profile = 55, Diameter = 16, Season = Season.Summer, Type = TireType.New,
        UnitPrice = 100m, Quantity = 5, MinStock = 2
    };
}
