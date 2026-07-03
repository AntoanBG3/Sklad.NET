using Microsoft.AspNetCore.Mvc;
using Sklad.Models;
using Sklad.Services;

namespace Sklad.Controllers;

public class MovementsController : Controller
{
    private readonly IInventoryService _inventory;

    public MovementsController(IInventoryService inventory) => _inventory = inventory;

    // GET: /Movements
    public async Task<IActionResult> Index(MovementType? type, int page = 1)
    {
        ViewBag.Type = type;
        var movements = await _inventory.GetMovementsAsync(type, page);
        return View(movements);
    }
}
