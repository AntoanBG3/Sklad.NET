using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

public class TiresController : Controller
{
    private readonly IInventoryService _inventory;
    private readonly IStringLocalizer<SharedResource> _l;

    public TiresController(IInventoryService inventory, IStringLocalizer<SharedResource> l)
    {
        _inventory = inventory;
        _l = l;
    }

    private string? CurrentUser => User.Identity?.Name;

    // GET: /Tires
    public async Task<IActionResult> Index(TireFilterViewModel filter)
    {
        var vm = new IndexViewModel
        {
            Results = await _inventory.SearchAsync(filter),
            Filter = filter,
            Stats = await _inventory.GetStatsAsync()
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

    // GET: /Tires/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var tire = await _inventory.GetTireAsync(id.Value, includeMovements: true);
        if (tire is null) return NotFound();
        ViewBag.MovementCount = await _inventory.CountMovementsAsync(id.Value);
        return View(tire);
    }

    // GET: /Tires/Create
    public IActionResult Create() => View(new CreateTireViewModel());

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

    // GET: /Tires/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var tire = await _inventory.GetTireAsync(id.Value);
        if (tire is null) return NotFound();
        return View(tire);
    }

    // POST: /Tires/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Version,Sku,Barcode,Brand,Model,Width,Profile,Diameter,Season,Type,UnitPrice,MinStock,Location")] Tire tire)
    {
        if (id != tire.Id) return NotFound();
        if (!ModelState.IsValid) return View(tire);
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
            ModelState.AddModelError(nameof(Tire.Sku), _l["A tire with SKU {0} already exists.", ex.Sku]);
            return View(tire);
        }
        catch (StaleTireException)
        {
            ModelState.AddModelError(string.Empty, _l["The tire was modified by someone else. Reload the page and try again."]);
            return View(tire);
        }
        TempData["Flash"] = _l["Tire {0} saved.", tire.Sku].Value;
        return RedirectToAction(nameof(Details), new { id = tire.Id });
    }

    // GET: /Tires/Delete/5
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

    // GET: /Tires/Report
    public async Task<IActionResult> Report()
    {
        var report = await _inventory.GetValueReportAsync();
        return View(report);
    }

    // GET: /Tires/RegisterMovement/5?type=In
    public async Task<IActionResult> RegisterMovement(int? id, MovementType? type = null)
    {
        if (id is null) return NotFound();
        var tire = await _inventory.GetTireAsync(id.Value);
        if (tire is null) return NotFound();
        ViewBag.Tire = tire;
        return View(new RegisterMovementViewModel { TireId = tire.Id, MovementType = type ?? MovementType.In });
    }

    // POST: /Tires/RegisterMovement
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterMovement(RegisterMovementViewModel vm)
    {
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
        ViewBag.Tire = await _inventory.GetTireAsync(vm.TireId);
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
}
