using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

[Authorize(Roles = nameof(UserRole.Admin))]
public class ShopSettingsController : Controller
{
    private readonly IShopSettingsService _settings;
    private readonly RequestLocalizationOptions _localizationOptions;
    private readonly IStringLocalizer<SharedResource> _l;

    public ShopSettingsController(
        IShopSettingsService settings,
        IOptions<RequestLocalizationOptions> localizationOptions,
        IStringLocalizer<SharedResource> l)
    {
        _settings = settings;
        _localizationOptions = localizationOptions.Value;
        _l = l;
    }

    // GET: /ShopSettings
    public async Task<IActionResult> Index()
        => View(ShopSettingsViewModel.FromSettings(await _settings.GetAsync()));

    // POST: /ShopSettings
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ShopSettingsViewModel vm)
    {
        // The select only offers supported values; this guards a forged POST.
        if (!string.IsNullOrWhiteSpace(vm.DefaultCulture) &&
            _localizationOptions.SupportedUICultures?.Any(c =>
                string.Equals(c.Name, vm.DefaultCulture, StringComparison.OrdinalIgnoreCase)) != true)
        {
            ModelState.AddModelError(nameof(ShopSettingsViewModel.DefaultCulture), _l["Choose a supported language."]);
        }
        if (!ModelState.IsValid) return View(vm);
        await _settings.SaveAsync(vm.ToSettings());
        TempData["Flash"] = _l["Settings saved."].Value;
        return RedirectToAction(nameof(Index));
    }
}
