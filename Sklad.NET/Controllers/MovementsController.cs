using Microsoft.AspNetCore.Mvc;
using Sklad.Models;
using Sklad.Services;

namespace Sklad.Controllers;

public class MovementsController : Controller
{
    private readonly IInventoryService _inventory;

    public MovementsController(IInventoryService inventory) => _inventory = inventory;

    // GET: /Movements?type=In&tireId=5
    public async Task<IActionResult> Index(MovementType? type, int? tireId, int page = 1)
    {
        ViewBag.Type = type;
        ViewBag.TireId = tireId;
        ViewBag.Tire = tireId.HasValue ? await _inventory.GetTireAsync(tireId.Value) : null;
        var movements = await _inventory.GetMovementsAsync(type, tireId, page);
        return View(movements);
    }
}
