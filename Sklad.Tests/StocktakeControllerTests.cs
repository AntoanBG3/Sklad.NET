using System.Reflection;
using Microsoft.AspNetCore.Authorization;
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

public sealed class StocktakeControllerTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_redirects_to_the_new_count_with_a_flash()
    {
        await using (var seed = _db.CreateContext())
        {
            seed.Tires.Add(NewTire());
            await seed.SaveChangesAsync();
        }
        await using var context = _db.CreateContext();
        var controller = Controller(context);

        var result = await controller.Create(new CreateStocktakeViewModel { Location = "A-1" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(StocktakesController.Details), redirect.ActionName);
        Assert.NotNull(redirect.RouteValues!["id"]);
        Assert.Contains("ST-0001", (string)controller.TempData["Flash"]!);
    }

    [Fact]
    public async Task Create_overlap_returns_form_with_a_model_error()
    {
        await using (var seed = _db.CreateContext())
        {
            seed.Tires.Add(NewTire());
            await seed.SaveChangesAsync();
            await Service(seed).CreateAsync(null, null);
        }
        await using var context = _db.CreateContext();
        var controller = Controller(context);

        var result = await controller.Create(new CreateStocktakeViewModel());

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Details_returns_a_counting_view_model()
    {
        int id;
        await using (var seed = _db.CreateContext())
        {
            seed.Tires.Add(NewTire());
            await seed.SaveChangesAsync();
            id = (await Service(seed).CreateAsync(null, null)).Id;
        }
        await using var context = _db.CreateContext();

        var result = Assert.IsType<ViewResult>(await Controller(context).Details(id));
        var model = Assert.IsType<StocktakeCountViewModel>(result.Model);

        Assert.Equal("ST-0001", model.Number);
        Assert.Single(model.Items);
    }

    [Fact]
    public async Task Print_returns_the_count_with_shop_identity()
    {
        int id;
        await using (var seed = _db.CreateContext())
        {
            seed.Tires.Add(NewTire());
            seed.ShopSettings.Add(new ShopSettings { Id = ShopSettings.SingletonId, Name = "Test Shop" });
            await seed.SaveChangesAsync();
            id = (await Service(seed).CreateAsync(null, null)).Id;
        }
        await using var context = _db.CreateContext();

        var result = Assert.IsType<ViewResult>(await Controller(context).Print(id));
        var model = Assert.IsType<StocktakePrintViewModel>(result.Model);

        Assert.Equal("Test Shop", model.Shop.Name);
        Assert.Equal("ST-0001", model.Count.Number);
    }

    [Fact]
    public async Task Complete_post_rejects_a_missing_version_token()
    {
        await using var context = _db.CreateContext();
        Assert.IsType<BadRequestResult>(await Controller(context).CompleteConfirmed(1, null));
    }

    [Fact]
    public async Task Save_uses_the_narrow_command_model_and_persists_counts()
    {
        int id, itemId;
        await using (var seed = _db.CreateContext())
        {
            seed.Tires.Add(NewTire());
            await seed.SaveChangesAsync();
            var stocktake = await Service(seed).CreateAsync(null, null);
            id = stocktake.Id;
            itemId = Assert.Single(stocktake.Items).Id;
        }
        await using var context = _db.CreateContext();
        var controller = Controller(context);

        var result = await controller.Save(id, new SaveStocktakeCountsViewModel
        {
            Id = id,
            Version = 0,
            Items = [new SaveStocktakeCountLineViewModel { Id = itemId, CountedQuantity = 3, Note = "short" }]
        });

        Assert.IsType<RedirectToActionResult>(result);
        await using var check = _db.CreateContext();
        var saved = Assert.Single((await Service(check).GetStocktakeAsync(id))!.Items);
        Assert.Equal(3, saved.CountedQuantity);
        Assert.Equal("short", saved.Note);
    }

    [Theory]
    [InlineData(nameof(StocktakesController.CompleteConfirmed))]
    [InlineData(nameof(StocktakesController.Refresh))]
    [InlineData(nameof(StocktakesController.Cancel))]
    public void Inventory_mutations_are_admin_only(string methodName)
    {
        var method = typeof(StocktakesController).GetMethod(methodName)!;
        var attribute = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(nameof(UserRole.Admin), attribute.Roles);
    }

    private static StocktakesController Controller(SkladDbContext context)
    {
        var http = new DefaultHttpContext();
        var inventory = new InventoryService(context, NullLogger<InventoryService>.Instance);
        var settings = new ShopSettingsService(
            context, NullLogger<ShopSettingsService>.Instance, new DefaultCultureCache());
        return new StocktakesController(Service(context), inventory, settings, new FakeLocalizer<SharedResource>())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NullTempDataProvider())
        };
    }

    private static StocktakeService Service(SkladDbContext context)
        => new(context, NullLogger<StocktakeService>.Instance);

    private static Tire NewTire() => new()
    {
        Sku = "COUNT-1", Brand = "Test", Model = "M", Width = 205, Profile = 55,
        Diameter = 16, Season = Season.Summer, Type = TireType.New, UnitPrice = 100m,
        Quantity = 5, MinStock = 2, Location = "A-1"
    };
}
