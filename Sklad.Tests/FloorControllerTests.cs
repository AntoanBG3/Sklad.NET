using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
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
        var service = new InventoryService(context, NullLogger<InventoryService>.Instance);
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

    // Sku and Barcode carry the Unicode-aware collation; this proves the lookup relies on it.
    [Fact]
    public async Task Tire_finds_a_tire_by_barcode_in_a_different_case()
    {
        await SeedAsync(NewTire("MI-206", barcode: "ABC123"));
        using var context = _db.CreateContext();

        var result = Assert.IsType<ViewResult>(await CreateController(context).Tire("abc123"));
        var vm = Assert.IsType<FloorBookViewModel>(result.Model);

        Assert.Equal("MI-206", vm.Sku);
    }

    [Fact]
    public async Task Book_an_In_raises_stock_and_records_the_user()
    {
        var tire = await SeedAsync(NewTire("MI-300", qty: 10));
        using var context = _db.CreateContext();

        var result = Assert.IsType<RedirectToActionResult>(
            await CreateController(context, "picker").Book(tire.Id, MovementType.In, 3));
        Assert.Equal(nameof(FloorController.Index), result.ActionName);

        await using var check = _db.CreateContext();
        Assert.Equal(13, (await check.Tires.FindAsync(tire.Id))!.Quantity);
        var movement = Assert.Single(check.StockMovements.Where(m => m.TireId == tire.Id));
        Assert.Equal(MovementType.In, movement.MovementType);
        Assert.Equal("picker", movement.UserName);
    }

    [Fact]
    public async Task Book_an_Out_beyond_stock_leaves_the_tire_untouched()
    {
        var tire = await SeedAsync(NewTire("MI-301", qty: 2));
        using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = Assert.IsType<ViewResult>(await controller.Book(tire.Id, MovementType.Out, 5));

        Assert.Equal(nameof(FloorController.Tire), result.ViewName);
        Assert.False(controller.ModelState.IsValid);

        await using var check = _db.CreateContext();
        Assert.Equal(2, (await check.Tires.FindAsync(tire.Id))!.Quantity);
        Assert.Empty(check.StockMovements.Where(m => m.TireId == tire.Id));
    }

    // Adjustment sets stock ABSOLUTELY and permits a quantity of zero, so a crafted
    // post would silently zero a tire. The floor books flows only. The out-of-range
    // case pins the guard as an allow-list: the enum binder accepts any int, so a
    // deny-list on Adjustment alone would let (MovementType)99 through.
    // A malformed or missing movementType fails model binding and arrives as null;
    // a non-nullable parameter would instead default to In and silently book one.
    [Theory]
    [InlineData(MovementType.Adjustment, 0)]
    [InlineData(MovementType.Adjustment, 99)]
    [InlineData((MovementType)99, 1)]
    [InlineData(null, 1)]
    public async Task Book_refuses_a_type_that_is_not_a_flow(MovementType? type, int quantity)
    {
        var tire = await SeedAsync(NewTire("MI-302", qty: 4));
        using var context = _db.CreateContext();

        var result = await CreateController(context).Book(tire.Id, type, quantity);

        Assert.IsType<BadRequestResult>(result);
        await using var check = _db.CreateContext();
        Assert.Equal(4, (await check.Tires.FindAsync(tire.Id))!.Quantity);
        Assert.Empty(check.StockMovements.Where(m => m.TireId == tire.Id));
    }

    [Fact]
    public async Task Book_a_zero_quantity_is_rejected()
    {
        var tire = await SeedAsync(NewTire("MI-303", qty: 4));
        using var context = _db.CreateContext();
        var controller = CreateController(context);

        Assert.IsType<ViewResult>(await controller.Book(tire.Id, MovementType.Out, 0));
        Assert.False(controller.ModelState.IsValid);

        await using var check = _db.CreateContext();
        Assert.Equal(4, (await check.Tires.FindAsync(tire.Id))!.Quantity);
    }

    [Fact]
    public async Task Book_an_overflowing_quantity_is_rejected_without_touching_stock()
    {
        var tire = await SeedAsync(NewTire("MI-OVERFLOW", qty: 1));
        using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = Assert.IsType<ViewResult>(
            await controller.Book(tire.Id, MovementType.In, int.MaxValue));

        Assert.Equal(nameof(FloorController.Tire), result.ViewName);
        Assert.False(controller.ModelState.IsValid);

        await using var check = _db.CreateContext();
        Assert.Equal(1, (await check.Tires.FindAsync(tire.Id))!.Quantity);
        Assert.Empty(check.StockMovements.Where(m => m.TireId == tire.Id));
    }

    [Fact]
    public async Task Book_a_stale_tire_re_renders_instead_of_returning_500()
    {
        var tire = await SeedAsync(NewTire("MI-304", qty: 4));
        using var context = _db.CreateContext(new BumpVersionsOnSave(_db.Connection));
        var controller = CreateController(context);

        var result = Assert.IsType<ViewResult>(
            await controller.Book(tire.Id, MovementType.In, 1));

        Assert.Equal(nameof(FloorController.Tire), result.ViewName);
        Assert.False(controller.ModelState.IsValid);

        await using var check = _db.CreateContext();
        Assert.Equal(4, (await check.Tires.FindAsync(tire.Id))!.Quantity);
        Assert.Empty(check.StockMovements.Where(m => m.TireId == tire.Id));
    }

    [Fact]
    public async Task Book_for_a_missing_tire_returns_to_the_scan_screen()
    {
        using var context = _db.CreateContext();

        var result = Assert.IsType<RedirectToActionResult>(
            await CreateController(context).Book(9999, MovementType.In, 1));

        Assert.Equal(nameof(FloorController.Index), result.ActionName);
    }
}
