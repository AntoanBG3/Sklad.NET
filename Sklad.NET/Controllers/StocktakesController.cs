using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

public sealed class StocktakesController(
    IStocktakeService stocktakes,
    IInventoryService inventory,
    IShopSettingsService settings,
    IStringLocalizer<SharedResource> _l) : Controller
{
    private string? CurrentUser => User.Identity?.Name;

    public async Task<IActionResult> Index(StocktakeStatus? status, int page = 1)
    {
        ViewBag.Status = status;
        var pageSize = (await settings.GetAsync()).PageSize ?? InventoryService.DefaultPageSize;
        return View(await stocktakes.GetStocktakesAsync(status, page, pageSize));
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var stocktake = await stocktakes.GetStocktakeAsync(id.Value);
        return stocktake is null ? NotFound() : View(StocktakeCountViewModel.FromStocktake(stocktake));
    }

    public async Task<IActionResult> Print(int? id)
    {
        if (id is null) return NotFound();
        var stocktake = await stocktakes.GetStocktakeAsync(id.Value);
        if (stocktake is null) return NotFound();
        return View(new StocktakePrintViewModel
        {
            Count = StocktakeCountViewModel.FromStocktake(stocktake),
            Shop = await settings.GetAsync()
        });
    }

    public async Task<IActionResult> Create()
    {
        await LoadLocationsAsync();
        return View(new CreateStocktakeViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateStocktakeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await LoadLocationsAsync();
            return View(model);
        }

        try
        {
            var stocktake = await stocktakes.CreateAsync(model.Location, model.Note, CurrentUser);
            TempData["Flash"] = _l["Stocktake {0} created with {1} lines.", stocktake.Number, stocktake.Items.Count].Value;
            return RedirectToAction(nameof(Details), new { id = stocktake.Id });
        }
        catch (EmptyStocktakeException)
        {
            ModelState.AddModelError(nameof(CreateStocktakeViewModel.Location),
                _l["The selected location contains no tires."]);
        }
        catch (ActiveStocktakeExistsException exception)
        {
            ModelState.AddModelError(string.Empty,
                _l["Some tires are already being counted in {0}. Complete or cancel it first.", exception.Number]);
        }

        await LoadLocationsAsync();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, SaveStocktakeCountsViewModel model)
    {
        if (id != model.Id) return NotFound();
        if (model.Version is null) return BadRequest();
        if (!ModelState.IsValid)
            return await ReloadWithSubmittedCountsAsync(id, model);

        try
        {
            await stocktakes.SaveCountsAsync(id, model.Version.Value, model.Items.Select(item => item.ToCount()).ToList());
            TempData["Flash"] = _l["Counts saved."].Value;
        }
        catch (StocktakeNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidStocktakeStateException)
        {
            TempData["Flash"] = _l["This stocktake is no longer open."].Value;
        }
        catch (StaleStocktakeException)
        {
            TempData["Flash"] = _l["The stocktake was modified by someone else. Reload and enter your counts again."].Value;
        }
        catch (InvalidStocktakeLinesException)
        {
            return BadRequest();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Complete(int? id)
    {
        if (id is null) return NotFound();
        var stocktake = await stocktakes.GetStocktakeAsync(id.Value);
        if (stocktake is null) return NotFound();
        if (!stocktake.IsOpen)
            return RedirectToAction(nameof(Details), new { id });
        return View(StocktakeCountViewModel.FromStocktake(stocktake));
    }

    [HttpPost, ActionName("Complete")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> CompleteConfirmed(int id, int? expectedVersion)
    {
        if (expectedVersion is null) return BadRequest();
        try
        {
            var variances = await stocktakes.CompleteAsync(id, expectedVersion.Value, CurrentUser);
            TempData["Flash"] = _l["Stocktake completed. {0} inventory adjustments were posted.", variances].Value;
        }
        catch (StocktakeNotFoundException)
        {
            return NotFound();
        }
        catch (IncompleteStocktakeException exception)
        {
            TempData["Flash"] = _l["Count all lines before completion. {0} remain.", exception.Remaining].Value;
        }
        catch (StocktakeInventoryChangedException exception)
        {
            TempData["Flash"] = _l["Stock changed for {0} counted tires. Refresh the snapshot and recount those lines.", exception.Skus.Count].Value;
        }
        catch (InvalidStocktakeStateException)
        {
            TempData["Flash"] = _l["This stocktake is no longer open."].Value;
        }
        catch (StaleStocktakeException)
        {
            TempData["Flash"] = _l["The stocktake was modified by someone else. Reload and try again."].Value;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Refresh(int id, int? expectedVersion)
    {
        if (expectedVersion is null) return BadRequest();
        try
        {
            var changed = await stocktakes.RefreshChangedAsync(id, expectedVersion.Value);
            TempData["Flash"] = changed == 0
                ? _l["The snapshot is current; no counts were cleared."].Value
                : _l["Snapshot refreshed. Recount the {0} cleared lines.", changed].Value;
        }
        catch (StocktakeNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidStocktakeStateException)
        {
            TempData["Flash"] = _l["This stocktake is no longer open."].Value;
        }
        catch (StaleStocktakeException)
        {
            TempData["Flash"] = _l["The stocktake was modified by someone else. Reload and try again."].Value;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Cancel(int id, int? expectedVersion)
    {
        if (expectedVersion is null) return BadRequest();
        try
        {
            await stocktakes.CancelAsync(id, expectedVersion.Value);
            TempData["Flash"] = _l["Stocktake cancelled. Inventory was not changed."].Value;
        }
        catch (StocktakeNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidStocktakeStateException)
        {
            TempData["Flash"] = _l["This stocktake is no longer open."].Value;
        }
        catch (StaleStocktakeException)
        {
            TempData["Flash"] = _l["The stocktake was modified by someone else. Reload and try again."].Value;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult> ReloadWithSubmittedCountsAsync(int id, SaveStocktakeCountsViewModel submitted)
    {
        var stocktake = await stocktakes.GetStocktakeAsync(id);
        if (stocktake is null) return NotFound();

        var model = StocktakeCountViewModel.FromStocktake(stocktake);
        var submittedById = submitted.Items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        foreach (var item in model.Items)
        {
            if (!submittedById.TryGetValue(item.Id, out var value)) continue;
            item.CountedQuantity = value.CountedQuantity;
            item.Note = value.Note;
        }
        return View("Details", model);
    }

    private async Task LoadLocationsAsync()
        => ViewBag.Locations = (await inventory.GetFilterOptionsAsync()).Locations;
}
