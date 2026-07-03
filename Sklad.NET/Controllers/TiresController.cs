using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sklad.Data;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

public class TiresController : Controller
{
    private readonly SkladDbContext _db;
    private readonly IInventoryService _inventory;

    public TiresController(SkladDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    // GET: /Tires
    public async Task<IActionResult> Index(TireFilterViewModel filter)
    {
        var tires = await _inventory.SearchAsync(
            filter.Sku, filter.Brand, filter.Model,
            filter.Width, filter.Profile, filter.Diameter,
            filter.Season, filter.Type);

        var stats = await _db.Tires
            .Select(t => new { t.Quantity, t.MinStock, t.UnitPrice })
            .ToListAsync();

        var vm = new IndexViewModel
        {
            Tires        = tires,
            Filter       = filter,
            TotalSkus    = stats.Count,
            TotalUnits   = stats.Sum(t => t.Quantity),
            LowStockCount = stats.Count(t => t.Quantity <= t.MinStock),
            TotalValue   = stats.Sum(t => (decimal)t.Quantity * t.UnitPrice)
        };

        return View(vm);
    }

    // GET: /Tires/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var tire = await _db.Tires
            .Include(t => t.StockMovements.OrderByDescending(m => m.Date))
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tire is null) return NotFound();
        return View(tire);
    }

    // GET: /Tires/Create
    public IActionResult Create() => View(new Tire());

    // POST: /Tires/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Sku,Brand,Model,Width,Profile,Diameter,Season,Type,UnitPrice,Quantity,MinStock,Location")] Tire tire)
    {
        if (!ModelState.IsValid) return View(tire);
        _db.Tires.Add(tire);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // GET: /Tires/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var tire = await _db.Tires.FindAsync(id);
        if (tire is null) return NotFound();
        return View(tire);
    }

    // POST: /Tires/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Sku,Brand,Model,Width,Profile,Diameter,Season,Type,UnitPrice,Quantity,MinStock,Location")] Tire tire)
    {
        if (id != tire.Id) return NotFound();
        if (!ModelState.IsValid) return View(tire);
        try
        {
            _db.Update(tire);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _db.Tires.AnyAsync(t => t.Id == id)) return NotFound();
            throw;
        }
        return RedirectToAction(nameof(Index));
    }

    // GET: /Tires/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var tire = await _db.Tires.FirstOrDefaultAsync(t => t.Id == id);
        if (tire is null) return NotFound();
        return View(tire);
    }

    // POST: /Tires/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var tire = await _db.Tires.FindAsync(id);
        if (tire is not null)
        {
            _db.Tires.Remove(tire);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // GET: /Tires/LowStock
    public async Task<IActionResult> LowStock()
    {
        var tires = await _inventory.GetLowStockAsync();
        return View(tires);
    }

    // GET: /Tires/RegisterMovement/5
    public async Task<IActionResult> RegisterMovement(int? id)
    {
        if (id is null) return NotFound();
        var tire = await _db.Tires.FindAsync(id);
        if (tire is null) return NotFound();
        ViewBag.Tire = tire;
        return View(new RegisterMovementViewModel { TireId = tire.Id });
    }

    // POST: /Tires/RegisterMovement
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterMovement(RegisterMovementViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Tire = await _db.Tires.FindAsync(vm.TireId);
            return View(vm);
        }
        try
        {
            await _inventory.RegisterMovementAsync(vm.TireId, vm.MovementType, vm.Quantity, vm.Note);
            return RedirectToAction(nameof(Details), new { id = vm.TireId });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.Tire = await _db.Tires.FindAsync(vm.TireId);
            return View(vm);
        }
    }

    // GET: /Tires/Export
    public async Task<IActionResult> Export(TireFilterViewModel? filter)
    {
        IEnumerable<Tire> tires;
        if (filter is not null && filter.HasAnyFilter)
            tires = await _inventory.SearchAsync(filter.Sku, filter.Brand, filter.Model,
                filter.Width, filter.Profile, filter.Diameter, filter.Season, filter.Type);
        else
            tires = await _db.Tires.OrderBy(t => t.Brand).ThenBy(t => t.Model).ToListAsync();

        var bytes = await _inventory.ExportCsvAsync(tires);
        return File(bytes, "text/csv", $"tires_{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
