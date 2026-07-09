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

public class PurchaseOrdersControllerTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static PurchaseOrdersController CreateController(SkladDbContext context, string? userName = null)
    {
        var purchasing = new PurchasingService(context, NullLogger<PurchasingService>.Instance);
        var inventory = new InventoryService(context, NullLogger<InventoryService>.Instance, new FakeLocalizer<SharedResource>());
        var httpContext = new DefaultHttpContext();
        if (userName is not null)
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, userName)], "test"));
        var settings = new ShopSettingsService(context, NullLogger<ShopSettingsService>.Instance);
        return new PurchaseOrdersController(purchasing, inventory, settings, new FakeLocalizer<SharedResource>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NullTempDataProvider()),
            ObjectValidator = new NoOpObjectValidator()
        };
    }

    private async Task<(int SupplierId, int TireId)> SeedAsync()
    {
        await using var context = _db.CreateContext();
        var supplier = new Supplier { Name = "Tyre Trade" };
        var tire = new Tire
        {
            Sku = "POC-1", Brand = "Test", Model = "M", Width = 205, Profile = 55, Diameter = 16,
            Season = Season.Summer, Type = TireType.New, UnitPrice = 100m, Quantity = 5, MinStock = 2
        };
        context.Suppliers.Add(supplier);
        context.Tires.Add(tire);
        await context.SaveChangesAsync();
        return (supplier.Id, tire.Id);
    }

    [Fact]
    public async Task Print_returns_the_order_with_the_shop_letterhead()
    {
        var (supplierId, tireId) = await SeedAsync();
        int orderId;
        await using (var context = _db.CreateContext())
        {
            var purchasing = new PurchasingService(context, NullLogger<PurchasingService>.Instance);
            var order = await purchasing.CreateOrderAsync(supplierId, "urgent", [new PurchaseOrderLine(tireId, 4, 80m)], "boss");
            orderId = order.Id;
            context.ShopSettings.Add(new ShopSettings { Id = ShopSettings.SingletonId, Name = "Гуми ЕООД", VatNumber = "BG123" });
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var result = Assert.IsType<ViewResult>(await CreateController(context).Print(orderId));
            var vm = Assert.IsType<PurchaseOrderPrintViewModel>(result.Model);

            Assert.Equal(orderId, vm.Order.Id);
            Assert.Single(vm.Order.Items);
            Assert.Equal("Гуми ЕООД", vm.Shop.Name);
            Assert.True(vm.HasLetterhead);
        }
    }

    [Fact]
    public async Task Print_without_configured_shop_settings_still_renders()
    {
        var (supplierId, tireId) = await SeedAsync();
        int orderId;
        await using (var context = _db.CreateContext())
        {
            var purchasing = new PurchasingService(context, NullLogger<PurchasingService>.Instance);
            orderId = (await purchasing.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 1, 10m)])).Id;
        }

        await using (var context = _db.CreateContext())
        {
            var result = Assert.IsType<ViewResult>(await CreateController(context).Print(orderId));
            var vm = Assert.IsType<PurchaseOrderPrintViewModel>(result.Model);

            Assert.False(vm.HasLetterhead);
            Assert.Null(vm.Shop.Name);
        }
    }

    [Fact]
    public async Task Print_of_an_unknown_order_is_not_found()
    {
        await using var context = _db.CreateContext();

        Assert.IsType<NotFoundResult>(await CreateController(context).Print(4242));
    }

    [Fact]
    public async Task Create_post_drops_blank_rows_and_creates_the_order()
    {
        var (supplierId, tireId) = await SeedAsync();
        await using var context = _db.CreateContext();
        var controller = CreateController(context, "boss");

        var vm = new PurchaseOrderFormViewModel
        {
            SupplierId = supplierId,
            Items =
            [
                new PurchaseOrderItemViewModel { TireId = tireId, Quantity = 4, UnitCost = 90m },
                new PurchaseOrderItemViewModel()
            ]
        };
        var result = await controller.Create(vm);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PurchaseOrdersController.Details), redirect.ActionName);

        var order = Assert.Single(await context.PurchaseOrders.Include(o => o.Items).ToListAsync());
        Assert.Equal("boss", order.CreatedBy);
        Assert.Equal(4, Assert.Single(order.Items).Quantity);
    }

    [Fact]
    public async Task Create_post_with_only_blank_rows_returns_view_with_error()
    {
        var (supplierId, _) = await SeedAsync();
        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var vm = new PurchaseOrderFormViewModel
        {
            SupplierId = supplierId,
            Items = [new PurchaseOrderItemViewModel()]
        };
        var result = await controller.Create(vm);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(0, await context.PurchaseOrders.CountAsync());
    }

    [Fact]
    public async Task Create_get_with_tireId_prefills_an_order_line()
    {
        var (_, tireId) = await SeedAsync();
        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(supplierId: null, tireId: tireId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PurchaseOrderFormViewModel>(view.Model);
        var item = Assert.Single(vm.Items);
        Assert.Equal(tireId, item.TireId);
        Assert.Null(item.Quantity);
        Assert.Null(item.UnitCost);
    }

    [Fact]
    public async Task Create_get_with_low_stock_tire_suggests_the_deficit_quantity()
    {
        int tireId;
        await using (var setup = _db.CreateContext())
        {
            var tire = new Tire
            {
                Sku = "LOW-1", Brand = "Test", Model = "M", Width = 195, Profile = 65, Diameter = 15,
                Season = Season.Winter, Type = TireType.New, UnitPrice = 80m, Quantity = 1, MinStock = 5
            };
            setup.Tires.Add(tire);
            await setup.SaveChangesAsync();
            tireId = tire.Id;
        }
        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(supplierId: null, tireId: tireId);

        var vm = Assert.IsType<PurchaseOrderFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(4, Assert.Single(vm.Items).Quantity);
    }

    [Fact]
    public async Task Create_get_with_unknown_tireId_falls_back_to_a_blank_line()
    {
        await SeedAsync();
        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(supplierId: null, tireId: 9999);

        var vm = Assert.IsType<PurchaseOrderFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(Assert.Single(vm.Items).IsBlank);
    }

    [Fact]
    public async Task Receive_post_updates_stock_and_redirects_to_details()
    {
        var (supplierId, tireId) = await SeedAsync();
        int orderId;
        await using (var setup = _db.CreateContext())
        {
            var service = new PurchasingService(setup, NullLogger<PurchasingService>.Instance);
            var order = await service.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 4, 90m)]);
            orderId = order.Id;
        }

        await using var context = _db.CreateContext();
        var controller = CreateController(context, "maria");

        var result = await controller.Receive(orderId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PurchaseOrdersController.Details), redirect.ActionName);
        Assert.Equal(9, (await context.Tires.FindAsync(tireId))!.Quantity);
    }

    [Fact]
    public async Task Receive_post_on_closed_order_flashes_instead_of_crashing()
    {
        var (supplierId, tireId) = await SeedAsync();
        int orderId;
        await using (var setup = _db.CreateContext())
        {
            var service = new PurchasingService(setup, NullLogger<PurchasingService>.Instance);
            var order = await service.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 4, 90m)]);
            await service.ReceiveAsync(order.Id);
            orderId = order.Id;
        }

        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = await controller.Receive(orderId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(controller.TempData["Flash"]);
        Assert.Equal(9, (await context.Tires.FindAsync(tireId))!.Quantity);
    }

    [Fact]
    public async Task Receive_post_on_unknown_order_returns_NotFound()
    {
        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        Assert.IsType<NotFoundResult>(await controller.Receive(4242));
    }
}

public class UsersControllerTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static UsersController CreateController(SkladDbContext context, int currentUserId)
    {
        var users = new UserService(context, NullLogger<UserService>.Instance);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString())], "test"))
        };
        return new UsersController(users, new FakeLocalizer<SharedResource>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NullTempDataProvider())
        };
    }

    private async Task<AppUser> SeedUserAsync(string username, UserRole role)
    {
        await using var context = _db.CreateContext();
        var users = new UserService(context, NullLogger<UserService>.Instance);
        return await users.CreateUserAsync(username, "password-123", role);
    }

    [Fact]
    public async Task Create_duplicate_username_returns_view_with_model_error()
    {
        var admin = await SeedUserAsync("boss", UserRole.Admin);
        await using var context = _db.CreateContext();
        var controller = CreateController(context, admin.Id);

        var result = await controller.Create(new CreateUserViewModel { Username = "boss", Password = "password-456", Role = UserRole.User });

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(CreateUserViewModel.Username)));
    }

    [Fact]
    public async Task Deleting_yourself_shows_error_instead_of_deleting()
    {
        var admin = await SeedUserAsync("boss", UserRole.Admin);
        await using var context = _db.CreateContext();
        var controller = CreateController(context, admin.Id);

        var result = await controller.DeleteConfirmed(admin.Id);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Delete", view.ViewName);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(1, await context.Users.CountAsync());
    }

    [Fact]
    public async Task Demoting_the_last_admin_shows_error()
    {
        var admin = await SeedUserAsync("boss", UserRole.Admin);
        await using var context = _db.CreateContext();
        var controller = CreateController(context, admin.Id);

        var result = await controller.Edit(admin.Id, new EditUserViewModel { Id = admin.Id, Username = "boss", Role = UserRole.User });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }
}
