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
        var inventory = new InventoryService(context, NullLogger<InventoryService>.Instance);
        var httpContext = new DefaultHttpContext();
        if (userName is not null)
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, userName)], "test"));
        return new PurchaseOrdersController(purchasing, inventory, new FakeLocalizer<SharedResource>())
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
