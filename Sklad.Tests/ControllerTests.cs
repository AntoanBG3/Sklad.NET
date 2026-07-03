using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sklad.Controllers;
using Sklad.Data;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Tests;

public class TiresControllerTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private TiresController CreateController(SkladDbContext context)
    {
        var service = new InventoryService(context, NullLogger<InventoryService>.Instance);
        var controller = new TiresController(service, new FakeLocalizer<SharedResource>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    private static Tire NewTire(string sku, int qty = 5) => new()
    {
        Sku = sku, Brand = "Test", Model = "M", Width = 205, Profile = 55, Diameter = 16,
        Season = Season.Summer, Type = TireType.New, UnitPrice = 100m, Quantity = qty, MinStock = 2
    };

    [Fact]
    public async Task RegisterMovement_post_returns_NotFound_when_tire_is_gone()
    {
        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = await controller.RegisterMovement(new RegisterMovementViewModel
        {
            TireId = 12345, MovementType = MovementType.In, Quantity = 1
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Create_duplicate_sku_returns_view_with_model_error()
    {
        await using (var seed = _db.CreateContext())
        {
            seed.Tires.Add(NewTire("DUP-9"));
            await seed.SaveChangesAsync();
        }

        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = await controller.Create(NewTire("DUP-9"));

        var view = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(Tire.Sku)));
        Assert.IsType<Tire>(view.Model);
    }

    [Fact]
    public async Task Delete_with_movements_returns_delete_view_with_error_instead_of_crashing()
    {
        int tireId;
        await using (var seed = _db.CreateContext())
        {
            var tire = NewTire("DEL-9");
            seed.Tires.Add(tire);
            await seed.SaveChangesAsync();
            tireId = tire.Id;
            var service = new InventoryService(seed, NullLogger<InventoryService>.Instance);
            await service.RegisterMovementAsync(tireId, MovementType.In, 1, null);
        }

        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = await controller.DeleteConfirmed(tireId);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Delete", view.ViewName);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task RegisterMovement_insufficient_stock_shows_error_not_500()
    {
        int tireId;
        await using (var seed = _db.CreateContext())
        {
            var tire = NewTire("OUT-9", qty: 2);
            seed.Tires.Add(tire);
            await seed.SaveChangesAsync();
            tireId = tire.Id;
        }

        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = await controller.RegisterMovement(new RegisterMovementViewModel
        {
            TireId = tireId, MovementType = MovementType.Out, Quantity = 10
        });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }
}

public class AccountControllerTests
{
    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class RecordingAuthService : IAuthenticationService
    {
        public ClaimsPrincipal? SignedIn { get; private set; }
        public bool SignedOut { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
            => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignedIn = principal;
            return Task.CompletedTask;
        }
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }
    }

    private static (AccountController Controller, RecordingAuthService Auth) CreateController(string? configuredUser, string? configuredPassword)
    {
        var settings = new Dictionary<string, string?>();
        if (configuredUser is not null) settings["Auth:Username"] = configuredUser;
        if (configuredPassword is not null) settings["Auth:Password"] = configuredPassword;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var auth = new RecordingAuthService();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(auth);
        services.AddSingleton<Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider,
            Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider>();
        services.AddSingleton<ITempDataDictionaryFactory>(new TempDataDictionaryFactory(new NullTempDataProvider()));
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        var controller = new AccountController(config, new FakeLocalizer<SharedResource>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        return (controller, auth);
    }

    [Fact]
    public async Task Login_with_wrong_password_shows_error_and_does_not_sign_in()
    {
        var (controller, auth) = CreateController("admin", "correct");

        var result = await controller.Login(new LoginViewModel { Username = "admin", Password = "wrong" }, null);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Null(auth.SignedIn);
    }

    [Fact]
    public async Task Login_with_correct_credentials_signs_in_and_redirects()
    {
        var (controller, auth) = CreateController("admin", "correct");

        var result = await controller.Login(new LoginViewModel { Username = "admin", Password = "correct" }, null);

        Assert.IsType<LocalRedirectResult>(result);
        Assert.NotNull(auth.SignedIn);
        Assert.Equal("admin", auth.SignedIn!.Identity?.Name);
    }

    [Fact]
    public async Task Login_fails_when_credentials_are_not_configured()
    {
        var (controller, auth) = CreateController(null, null);

        var result = await controller.Login(new LoginViewModel { Username = "admin", Password = "anything" }, null);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Null(auth.SignedIn);
    }
}

public class CultureControllerTests
{
    private static CultureController CreateController(out HttpContext httpContext)
    {
        var options = Options.Create(new Microsoft.AspNetCore.Builder.RequestLocalizationOptions());
        options.Value.AddSupportedUICultures("bg-BG", "en-GB");

        httpContext = new DefaultHttpContext();
        return new CultureController(options)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public void Set_supported_culture_writes_cookie()
    {
        var controller = CreateController(out var httpContext);

        controller.Set("en-GB", "/Tires");

        Assert.Contains(".AspNetCore.Culture", httpContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public void Set_unsupported_culture_writes_no_cookie()
    {
        var controller = CreateController(out var httpContext);

        controller.Set("fr-FR", "/Tires");

        Assert.Empty(httpContext.Response.Headers.SetCookie.ToString());
    }
}
