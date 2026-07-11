using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Sklad.Helpers;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

public class TiresController : Controller
{
    private readonly IInventoryService _inventory;
    private readonly IExcelExportService _excel;
    private readonly IShopSettingsService _settings;
    private readonly IStringLocalizer<SharedResource> _l;

    public TiresController(
        IInventoryService inventory,
        IExcelExportService excel,
        IShopSettingsService settings,
        IStringLocalizer<SharedResource> l)
    {
        _inventory = inventory;
        _excel = excel;
        _settings = settings;
        _l = l;
    }

    private string? CurrentUser => User.Identity?.Name;

    // GET: /Tires
    public async Task<IActionResult> Index(TireFilterViewModel filter)
    {
        var shop = await _settings.GetAsync();
        var vm = new IndexViewModel
        {
            Results = await _inventory.SearchAsync(filter, shop.PageSize ?? InventoryService.DefaultPageSize),
            Filter = filter,
            Stats = await _inventory.GetStatsAsync(),
            Options = await _inventory.GetFilterOptionsAsync()
        };
        return View(vm);
    }

    // GET: /Tires/Scan?code=...
    public async Task<IActionResult> Scan(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return RedirectToAction(nameof(Index));

        var tire = await _inventory.FindByCodeAsync(code.Trim());
        if (tire is null)
        {
            TempData["ScanMessage"] = _l["No tire matches code {0}.", code.Trim()].Value;
            return RedirectToAction(nameof(Index));
        }
        return RedirectToAction(nameof(Details), new { id = tire.Id });
    }

    // GET: /Tires/Details/5?returnUrl=... (Back returns to where the user came from)
    public async Task<IActionResult> Details(int? id, string? returnUrl = null)
    {
        if (id is null) return NotFound();
        var tire = await _inventory.GetTireAsync(id.Value, includeMovements: true);
        if (tire is null) return NotFound();
        ViewBag.MovementCount = await _inventory.CountMovementsAsync(id.Value);
        ViewBag.ReturnUrl = returnUrl;
        return View(tire);
    }

    // GET: /Tires/Create
    public async Task<IActionResult> Create() =>
        View(new CreateTireViewModel { MinStock = (await _settings.GetAsync()).DefaultMinStock });

    // POST: /Tires/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateTireViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var tire = vm.ToTire();
        try
        {
            await _inventory.CreateTireAsync(tire, CurrentUser);
        }
        catch (DuplicateSkuException ex)
        {
            ModelState.AddModelError(nameof(CreateTireViewModel.Sku), _l["A tire with SKU {0} already exists.", ex.Sku]);
            return View(vm);
        }
        TempData["Flash"] = _l["Tire {0} created.", tire.Sku].Value;
        return RedirectToAction(nameof(Details), new { id = tire.Id });
    }

    // GET: /Tires/Edit/5?returnUrl=... (Cancel returns to where the user came from)
    public async Task<IActionResult> Edit(int? id, string? returnUrl = null)
    {
        if (id is null) return NotFound();
        var tire = await _inventory.GetTireAsync(id.Value);
        if (tire is null) return NotFound();
        ViewBag.ReturnUrl = returnUrl;
        return View(EditTireViewModel.FromTire(tire));
    }

    // POST: /Tires/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditTireViewModel vm, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        if (id != vm.Id) return NotFound();
        if (!ModelState.IsValid) return View(vm);
        var tire = vm.ToTire();
        try
        {
            await _inventory.UpdateTireAsync(tire);
        }
        catch (TireNotFoundException)
        {
            return NotFound();
        }
        catch (DuplicateSkuException ex)
        {
            ModelState.AddModelError(nameof(EditTireViewModel.Sku), _l["A tire with SKU {0} already exists.", ex.Sku]);
            return View(vm);
        }
        catch (StaleTireException)
        {
            ModelState.AddModelError(string.Empty, _l["The tire was modified by someone else. Reload the page and try again."]);
            return View(vm);
        }
        TempData["Flash"] = _l["Tire {0} saved.", tire.Sku].Value;
        if (returnUrl is not null) return Redirect(Redirects.Safe(returnUrl));
        return RedirectToAction(nameof(Details), new { id = tire.Id });
    }

    // GET: /Tires/Delete/5
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var tire = await _inventory.GetTireAsync(id.Value);
        if (tire is null) return NotFound();
        ViewBag.HasMovements = await _inventory.CountMovementsAsync(id.Value) > 0;
        return View(tire);
    }

    // POST: /Tires/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var tire = await _inventory.GetTireAsync(id);
        try
        {
            await _inventory.DeleteTireAsync(id);
        }
        catch (TireHasMovementsException)
        {
            if (tire is null) return RedirectToAction(nameof(Index));
            ModelState.AddModelError(string.Empty, _l["This tire has movement records and cannot be deleted."]);
            return View("Delete", tire);
        }
        catch (TireOnOrderException)
        {
            if (tire is null) return RedirectToAction(nameof(Index));
            ModelState.AddModelError(string.Empty, _l["This tire appears on purchase orders and cannot be deleted."]);
            return View("Delete", tire);
        }
        if (tire is not null)
            TempData["Flash"] = _l["Tire {0} deleted.", tire.Sku].Value;
        return RedirectToAction(nameof(Index));
    }

    // GET: /Tires/LowStock
    public async Task<IActionResult> LowStock()
    {
        var tires = await _inventory.GetLowStockAsync();
        return View(tires);
    }

    private const int MaxSpanDays = 3660;

    // GET: /Tires/Report?from=2026-01-01&to=2026-07-09
    public async Task<IActionResult> Report(DateOnly? from = null, DateOnly? to = null)
    {
        var today = DateOnly.FromDateTime(Dates.Shop(DateTime.UtcNow));
        // Endpoints are inclusive, so months − 1 yields exactly that many monthly buckets.
        var months = (await _settings.GetAsync()).ReportRangeMonths ?? 12;
        var defaultFrom = today.AddMonths(-(months - 1));

        var start = from ?? defaultFrom;
        var end = to ?? today;

        if (start > end)
        {
            ModelState.AddModelError(string.Empty, _l["The start date cannot be after the end date."]);
            (start, end) = (defaultFrom, today);
        }
        else if (end.DayNumber - start.DayNumber > MaxSpanDays)
        {
            ModelState.AddModelError(string.Empty, _l["Choose a date range of ten years or less."]);
            (start, end) = (defaultFrom, today);
        }

        return View(new ValueReportViewModel
        {
            Value = await _inventory.GetValueReportAsync(),
            Trend = await _inventory.GetMovementTrendAsync(start, end),
            From = start,
            To = end
        });
    }

    // GET: /Tires/RegisterMovement/5?type=In&returnUrl=...
    public async Task<IActionResult> RegisterMovement(int? id, MovementType? type = null, string? returnUrl = null)
    {
        if (id is null) return NotFound();
        var tire = await _inventory.GetTireAsync(id.Value);
        if (tire is null) return NotFound();
        ViewBag.Tire = tire;
        ViewBag.ReturnUrl = returnUrl;
        return View(new RegisterMovementViewModel { TireId = tire.Id, MovementType = type ?? MovementType.In });
    }

    // POST: /Tires/RegisterMovement
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterMovement(RegisterMovementViewModel vm, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        var tire = await _inventory.GetTireAsync(vm.TireId);
        if (tire is null) return NotFound();

        if (!ModelState.IsValid)
        {
            ViewBag.Tire = tire;
            return View(vm);
        }
        try
        {
            var newQuantity = await _inventory.RegisterMovementAsync(vm.TireId, vm.MovementType, vm.Quantity!.Value, vm.Note, CurrentUser);
            TempData["Flash"] = _l["Movement recorded — stock is now {0}.", newQuantity].Value;
            if (returnUrl is not null) return Redirect(Redirects.Safe(returnUrl));
            return RedirectToAction(nameof(Details), new { id = vm.TireId });
        }
        catch (TireNotFoundException)
        {
            return NotFound();
        }
        catch (InsufficientStockException ex)
        {
            ModelState.AddModelError(string.Empty, _l["Insufficient stock. Available: {0}, requested: {1}.", ex.Available, ex.Requested]);
        }
        catch (InvalidMovementQuantityException)
        {
            ModelState.AddModelError(nameof(RegisterMovementViewModel.Quantity), _l["Quantity must be at least 1 for In/Out movements."]);
        }
        catch (StaleTireException)
        {
            ModelState.AddModelError(string.Empty, _l["The tire was modified by someone else. Reload the page and try again."]);
        }
        ViewBag.Tire = tire;
        return View(vm);
    }

    // GET: /Tires/Export
    public async Task<IActionResult> Export(TireFilterViewModel filter)
    {
        filter.Page = 1;
        var result = await _inventory.SearchAsync(filter, pageSize: int.MaxValue);
        var bytes = await _inventory.ExportCsvAsync(result.Items);
        return File(bytes, "text/csv", $"tires_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // GET: /Tires/ExportExcel
    public async Task<IActionResult> ExportExcel(TireFilterViewModel filter)
    {
        filter.Page = 1;
        var result = await _inventory.SearchAsync(filter, pageSize: int.MaxValue);
        var bytes = _excel.ExportTires(result.Items);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"tires_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }
}
