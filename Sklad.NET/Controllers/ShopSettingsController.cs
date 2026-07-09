using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

[Authorize(Roles = nameof(UserRole.Admin))]
public class ShopSettingsController : Controller
{
    private readonly IShopSettingsService _settings;
    private readonly IStringLocalizer<SharedResource> _l;

    public ShopSettingsController(IShopSettingsService settings, IStringLocalizer<SharedResource> l)
    {
        _settings = settings;
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
        if (!ModelState.IsValid) return View(vm);
        await _settings.SaveAsync(vm.ToSettings());
        TempData["Flash"] = _l["Shop details saved."].Value;
        return RedirectToAction(nameof(Index));
    }
}
