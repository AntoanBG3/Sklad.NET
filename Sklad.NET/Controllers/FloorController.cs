using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Sklad.Models;
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

    // POST: /Floor/Book
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Book(int tireId, MovementType? movementType, int quantity)
    {
        // Adjustment sets stock absolutely and permits a quantity of zero, so a
        // crafted post would zero a tire. The floor books flows only. The type is
        // nullable because a malformed or missing value otherwise binds to
        // default(MovementType), which is In, and would silently book one.
        if (movementType is not (MovementType.In or MovementType.Out))
            return BadRequest();

        var tire = await _inventory.GetTireAsync(tireId);
        if (tire is null)
        {
            TempData["Flash"] = _l["That tire no longer exists."].Value;
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var newQuantity = await _inventory.RegisterMovementAsync(
                tireId, movementType.Value, quantity, note: null, CurrentUser);
            TempData["Flash"] = _l["{0}: {1} in stock.", tire.Sku, newQuantity].Value;
            return RedirectToAction(nameof(Index));
        }
        catch (InsufficientStockException ex)
        {
            ModelState.AddModelError(string.Empty, _l["Only {0} in stock.", ex.Available]);
        }
        catch (InvalidMovementQuantityException)
        {
            // Model-level: the screen shows a ModelOnly summary and no per-field slot.
            ModelState.AddModelError(string.Empty, _l["Enter a quantity of at least 1."]);
        }
        catch (StockQuantityOverflowException)
        {
            ModelState.AddModelError(string.Empty, _l["The resulting stock quantity is too large."]);
        }
        catch (StaleTireException)
        {
            ModelState.AddModelError(string.Empty,
                _l["The tire was modified by someone else. Reload the page and try again."]);
        }
        catch (TireNotFoundException)
        {
            TempData["Flash"] = _l["That tire no longer exists."].Value;
            return RedirectToAction(nameof(Index));
        }

        return View(nameof(Tire), FloorBookViewModel.FromTire(tire));
    }
}
