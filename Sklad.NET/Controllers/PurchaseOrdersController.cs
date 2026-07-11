using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

public class PurchaseOrdersController : Controller
{
    private readonly IPurchasingService _purchasing;
    private readonly IInventoryService _inventory;
    private readonly IShopSettingsService _settings;
    private readonly IStringLocalizer<SharedResource> _l;

    public PurchaseOrdersController(
        IPurchasingService purchasing,
        IInventoryService inventory,
        IShopSettingsService settings,
        IStringLocalizer<SharedResource> l)
    {
        _purchasing = purchasing;
        _inventory = inventory;
        _settings = settings;
        _l = l;
    }

    private string? CurrentUser => User.Identity?.Name;

    // GET: /PurchaseOrders?status=Draft&supplierId=5
    public async Task<IActionResult> Index(PurchaseOrderStatus? status, int? supplierId, int page = 1)
    {
        ViewBag.Status = status;
        ViewBag.SupplierId = supplierId;
        ViewBag.Supplier = supplierId.HasValue ? await _purchasing.GetSupplierAsync(supplierId.Value) : null;
        var pageSize = (await _settings.GetAsync()).PageSize ?? InventoryService.DefaultPageSize;
        var orders = await _purchasing.GetOrdersAsync(status, supplierId, page, pageSize);
        return View(orders);
    }

    // GET: /PurchaseOrders/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var order = await _purchasing.GetOrderAsync(id.Value);
        if (order is null) return NotFound();
        return View(order);
    }

    // GET: /PurchaseOrders/Print/5
    public async Task<IActionResult> Print(int? id)
    {
        if (id is null) return NotFound();
        var order = await _purchasing.GetOrderAsync(id.Value);
        if (order is null) return NotFound();
        return View(new PurchaseOrderPrintViewModel { Order = order, Shop = await _settings.GetAsync() });
    }

    // GET: /PurchaseOrders/Create?supplierId=5&tireId=12
    public async Task<IActionResult> Create(int? supplierId, int? tireId)
    {
        await LoadFormOptionsAsync();
        var item = new PurchaseOrderItemViewModel();
        if (tireId is int id && await _inventory.GetTireAsync(id) is { } tire)
        {
            item.TireId = tire.Id;
            var deficit = tire.MinStock - tire.Quantity;
            if (deficit > 0)
                item.Quantity = deficit;
        }
        return View(new PurchaseOrderFormViewModel
        {
            SupplierId = supplierId,
            Items = [item]
        });
    }

    // POST: /PurchaseOrders/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PurchaseOrderFormViewModel vm)
    {
        PruneBlankItems(vm);
        if (!ModelState.IsValid)
        {
            await LoadFormOptionsAsync();
            return View(vm);
        }
        try
        {
            var order = await _purchasing.CreateOrderAsync(vm.SupplierId!.Value, Clean(vm.Note), vm.ToLines(), CurrentUser);
            TempData["Flash"] = _l["Purchase order {0} created.", order.Number].Value;
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
        catch (PurchasingException ex)
        {
            AddOrderError(ex);
        }
        catch (TireNotFoundException)
        {
            ModelState.AddModelError(string.Empty, _l["One of the selected tires no longer exists."]);
        }
        await LoadFormOptionsAsync();
        return View(vm);
    }

    // GET: /PurchaseOrders/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var order = await _purchasing.GetOrderAsync(id.Value);
        if (order is null) return NotFound();
        if (!order.IsEditable)
        {
            TempData["Flash"] = _l["Only draft orders can be edited."].Value;
            return RedirectToAction(nameof(Details), new { id });
        }
        await LoadFormOptionsAsync();
        return View(PurchaseOrderFormViewModel.FromOrder(order));
    }

    // POST: /PurchaseOrders/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PurchaseOrderFormViewModel vm)
    {
        if (id != vm.Id) return NotFound();
        PruneBlankItems(vm);
        if (!ModelState.IsValid)
        {
            await LoadFormOptionsAsync();
            return View(vm);
        }
        try
        {
            await _purchasing.UpdateDraftAsync(id, vm.SupplierId!.Value, Clean(vm.Note), vm.ToLines());
            TempData["Flash"] = _l["Purchase order saved."].Value;
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (PurchaseOrderNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOrderStateException)
        {
            TempData["Flash"] = _l["Only draft orders can be edited."].Value;
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (PurchasingException ex)
        {
            AddOrderError(ex);
        }
        catch (TireNotFoundException)
        {
            ModelState.AddModelError(string.Empty, _l["One of the selected tires no longer exists."]);
        }
        await LoadFormOptionsAsync();
        return View(vm);
    }

    // POST: /PurchaseOrders/MarkOrdered/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkOrdered(int id)
    {
        try
        {
            await _purchasing.MarkOrderedAsync(id);
            TempData["Flash"] = _l["Purchase order marked as ordered."].Value;
        }
        catch (PurchaseOrderNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOrderStateException)
        {
            TempData["Flash"] = _l["The order status no longer allows this action."].Value;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /PurchaseOrders/Receive/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(int id)
    {
        try
        {
            await _purchasing.ReceiveAsync(id, CurrentUser);
            TempData["Flash"] = _l["Purchase order received — stock updated."].Value;
        }
        catch (PurchaseOrderNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOrderStateException)
        {
            TempData["Flash"] = _l["The order status no longer allows this action."].Value;
        }
        catch (StaleTireException)
        {
            TempData["Flash"] = _l["The stock changed while receiving. Try again."].Value;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /PurchaseOrders/Cancel/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        try
        {
            await _purchasing.CancelAsync(id);
            TempData["Flash"] = _l["Purchase order cancelled."].Value;
        }
        catch (PurchaseOrderNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOrderStateException)
        {
            TempData["Flash"] = _l["The order status no longer allows this action."].Value;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    // Rows the user added and left untouched shouldn't block the submit — a
    // fully blank row binds as null or as an empty item. Re-validating after the
    // prune also renumbers ModelState keys to match the re-rendered form, whose
    // rows always use sequential indexes.
    private void PruneBlankItems(PurchaseOrderFormViewModel vm)
    {
        vm.Items = vm.Items.Where(i => i is not null && !i.IsBlank).ToList();
        ModelState.Clear();
        TryValidateModel(vm);
        if (vm.Items.Count == 0)
            ModelState.AddModelError(string.Empty, _l["A purchase order needs at least one line."]);
    }

    private void AddOrderError(PurchasingException ex)
    {
        var message = ex switch
        {
            SupplierNotFoundException => _l["The selected supplier no longer exists."],
            EmptyPurchaseOrderException => _l["A purchase order needs at least one line."],
            InvalidOrderLineException => _l["Order lines need a quantity of at least 1 and a non-negative cost."],
            _ => _l["The order could not be saved."]
        };
        ModelState.AddModelError(string.Empty, message);
    }

    private async Task LoadFormOptionsAsync()
    {
        ViewBag.Suppliers = (await _purchasing.GetSuppliersAsync()).Select(s => s.Supplier).ToList();
        ViewBag.Tires = (await _inventory.SearchAsync(new TireFilterViewModel(), pageSize: int.MaxValue)).Items;
    }

    private static string? Clean(string? note)
        => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
