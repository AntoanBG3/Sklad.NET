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

public class ShopSettingsControllerTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static ShopSettingsController CreateController(SkladDbContext context)
    {
        var settings = new ShopSettingsService(context, NullLogger<ShopSettingsService>.Instance);
        var httpContext = new DefaultHttpContext();
        return new ShopSettingsController(settings, new FakeLocalizer<SharedResource>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NullTempDataProvider()),
            ObjectValidator = new NoOpObjectValidator()
        };
    }

    [Fact]
    public async Task Index_on_a_fresh_install_returns_a_blank_view_model()
    {
        await using var context = _db.CreateContext();

        var result = Assert.IsType<ViewResult>(await CreateController(context).Index());
        var vm = Assert.IsType<ShopSettingsViewModel>(result.Model);

        Assert.Null(vm.Name);
    }

    [Fact]
    public async Task Index_returns_the_saved_settings()
    {
        await using (var context = _db.CreateContext())
        {
            context.ShopSettings.Add(new ShopSettings { Id = ShopSettings.SingletonId, Name = "Гуми ЕООД" });
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var result = Assert.IsType<ViewResult>(await CreateController(context).Index());
            var vm = Assert.IsType<ShopSettingsViewModel>(result.Model);

            Assert.Equal("Гуми ЕООД", vm.Name);
        }
    }

    [Fact]
    public async Task Post_saves_and_redirects_with_a_flash()
    {
        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var vm = new ShopSettingsViewModel { Name = "  Гуми ЕООД  ", Email = "shop@example.bg" };
        var result = Assert.IsType<RedirectToActionResult>(await controller.Index(vm));

        Assert.Equal(nameof(ShopSettingsController.Index), result.ActionName);
        Assert.NotNull(controller.TempData["Flash"]);
        Assert.Equal("Гуми ЕООД", context.ShopSettings.Single().Name);
    }

    [Fact]
    public async Task Post_with_an_invalid_model_redisplays_the_form_and_saves_nothing()
    {
        await using var context = _db.CreateContext();
        var controller = CreateController(context);
        controller.ModelState.AddModelError(nameof(ShopSettingsViewModel.Email), "invalid");

        var vm = new ShopSettingsViewModel { Name = "Гуми ЕООД", Email = "not-an-email" };
        var result = Assert.IsType<ViewResult>(await controller.Index(vm));

        Assert.Same(vm, result.Model);
        Assert.Empty(context.ShopSettings);
    }

    [Fact]
    public void Controller_is_restricted_to_administrators()
    {
        var attribute = Assert.Single(
            typeof(ShopSettingsController).GetCustomAttributes(
                typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: false));

        Assert.Equal(nameof(UserRole.Admin),
            ((Microsoft.AspNetCore.Authorization.AuthorizeAttribute)attribute).Roles);
    }
}
