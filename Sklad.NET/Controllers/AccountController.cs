using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Sklad.ViewModels;

namespace Sklad.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly IConfiguration _config;
    private readonly IStringLocalizer<SharedResource> _l;

    public AccountController(IConfiguration config, IStringLocalizer<SharedResource> l)
    {
        _config = config;
        _l = l;
    }

    // GET: /Account/Login
    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(SafeReturnUrl(returnUrl));
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginViewModel());
    }

    // POST: /Account/Login
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return View(vm);

        var configuredUser = _config["Auth:Username"];
        var configuredPassword = _config["Auth:Password"];

        if (string.IsNullOrEmpty(configuredUser) || string.IsNullOrEmpty(configuredPassword) ||
            !FixedTimeEquals(vm.Username, configuredUser) || !FixedTimeEquals(vm.Password, configuredPassword))
        {
            ModelState.AddModelError(string.Empty, _l["Invalid username or password."]);
            return View(vm);
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, configuredUser)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return LocalRedirect(SafeReturnUrl(returnUrl));
    }

    // POST: /Account/Logout
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    private static string SafeReturnUrl(string? returnUrl)
        => Sklad.Helpers.Redirects.Safe(returnUrl);

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
