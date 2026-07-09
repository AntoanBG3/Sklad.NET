using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Sklad.Controllers;
using Sklad.Data;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Tests;

public class FloorControllerTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private FloorController CreateController(SkladDbContext context, string userName = "picker")
    {
        var service = new InventoryService(context, NullLogger<InventoryService>.Instance, new FakeLocalizer<SharedResource>());
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, userName) }, "Test"))
        };
        return new FloorController(service, new FakeLocalizer<SharedResource>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NullTempDataProvider())
        };
    }

    private static Tire NewTire(string sku, int qty = 10, string? barcode = null) => new()
    {
        Sku = sku, Brand = "Michelin", Model = "Primacy 4", Width = 205, Profile = 55, Diameter = 16,
        Season = Season.Summer, Type = TireType.New, UnitPrice = 100m, Quantity = qty, MinStock = 2,
        Barcode = barcode, Location = "A-12"
    };

    private async Task<Tire> SeedAsync(Tire tire)
    {
        await using var context = _db.CreateContext();
        context.Tires.Add(tire);
        await context.SaveChangesAsync();
        return tire;
    }

    [Fact]
    public void Index_renders_the_scan_screen()
    {
        using var context = _db.CreateContext();
        var result = Assert.IsType<ViewResult>(CreateController(context).Index());
        var vm = Assert.IsType<FloorScanViewModel>(result.Model);
        Assert.False(vm.NotFound);
    }

    [Fact]
    public async Task Tire_with_a_blank_code_returns_to_the_scan_screen()
    {
        using var context = _db.CreateContext();
        var result = Assert.IsType<RedirectToActionResult>(await CreateController(context).Tire("   "));
        Assert.Equal(nameof(FloorController.Index), result.ActionName);
    }

    [Fact]
    public async Task Tire_with_an_unknown_code_re_renders_the_scan_screen()
    {
        using var context = _db.CreateContext();

        var result = Assert.IsType<ViewResult>(await CreateController(context).Tire("NOPE-1"));

        Assert.Equal(nameof(FloorController.Index), result.ViewName);
        var vm = Assert.IsType<FloorScanViewModel>(result.Model);
        Assert.True(vm.NotFound);
        Assert.Equal("NOPE-1", vm.Code);
    }

    [Fact]
    public async Task Tire_finds_a_tire_by_sku()
    {
        var tire = await SeedAsync(NewTire("MI-205", qty: 7));
        using var context = _db.CreateContext();

        var result = Assert.IsType<ViewResult>(await CreateController(context).Tire("MI-205"));
        var vm = Assert.IsType<FloorBookViewModel>(result.Model);

        Assert.Equal(tire.Id, vm.TireId);
        Assert.Equal(7, vm.Quantity);
        Assert.Equal("A-12", vm.Location);
    }

    // Sku and Barcode carry a NOCASE collation; this proves the lookup relies on it.
    [Fact]
    public async Task Tire_finds_a_tire_by_barcode_in_a_different_case()
    {
        await SeedAsync(NewTire("MI-206", barcode: "ABC123"));
        using var context = _db.CreateContext();

        var result = Assert.IsType<ViewResult>(await CreateController(context).Tire("abc123"));
        var vm = Assert.IsType<FloorBookViewModel>(result.Model);

        Assert.Equal("MI-206", vm.Sku);
    }
}
