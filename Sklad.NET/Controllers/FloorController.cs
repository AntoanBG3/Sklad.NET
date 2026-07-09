using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

public class FloorController : Controller
{
    private readonly IInventoryService _inventory;
    private readonly IStringLocalizer<SharedResource> _l;

    public FloorController(IInventoryService inventory, IStringLocalizer<SharedResource> l)
    {
        _inventory = inventory;
        _l = l;
    }

    private string? CurrentUser => User.Identity?.Name;

    // GET: /Floor
    public IActionResult Index() => View(new FloorScanViewModel());

    // GET: /Floor/Tire?code=...
    public async Task<IActionResult> Tire(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return RedirectToAction(nameof(Index));

        var trimmed = code.Trim();
        var tire = await _inventory.FindByCodeAsync(trimmed);
        if (tire is null)
            return View(nameof(Index), new FloorScanViewModel { Code = trimmed, NotFound = true });

        return View(FloorBookViewModel.FromTire(tire));
    }
}
