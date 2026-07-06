using Microsoft.AspNetCore.Mvc;
using Sklad.Models;
using Sklad.Services;

namespace Sklad.Controllers;

public class MovementsController : Controller
{
    private readonly IInventoryService _inventory;
    private readonly IExcelExportService _excel;

    public MovementsController(IInventoryService inventory, IExcelExportService excel)
    {
        _inventory = inventory;
        _excel = excel;
    }

    // GET: /Movements?type=In&tireId=5&from=2026-07-01&to=2026-07-05
    public async Task<IActionResult> Index(MovementType? type, int? tireId, DateOnly? from = null, DateOnly? to = null, int page = 1)
    {
        ViewBag.Type = type;
        ViewBag.TireId = tireId;
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.Tire = tireId.HasValue ? await _inventory.GetTireAsync(tireId.Value) : null;
        var movements = await _inventory.GetMovementsAsync(type, tireId, from, to, page);
        return View(movements);
    }

    // GET: /Movements/Export?type=In&tireId=5&from=2026-07-01&to=2026-07-05
    public async Task<IActionResult> Export(MovementType? type, int? tireId, DateOnly? from = null, DateOnly? to = null)
    {
        var movements = await _inventory.GetMovementsAsync(type, tireId, from, to, page: 1, pageSize: int.MaxValue);
        var bytes = _excel.ExportMovements(movements.Items);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"movements_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }
}
