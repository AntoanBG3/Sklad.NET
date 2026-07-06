using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

public class SuppliersController : Controller
{
    private readonly IPurchasingService _purchasing;
    private readonly IStringLocalizer<SharedResource> _l;

    public SuppliersController(IPurchasingService purchasing, IStringLocalizer<SharedResource> l)
    {
        _purchasing = purchasing;
        _l = l;
    }

    // GET: /Suppliers
    public async Task<IActionResult> Index()
        => View(await _purchasing.GetSuppliersAsync());

    // GET: /Suppliers/Create
    public IActionResult Create() => View(new SupplierViewModel());

    // POST: /Suppliers/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SupplierViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var supplier = vm.ToSupplier();
        try
        {
            await _purchasing.CreateSupplierAsync(supplier);
        }
        catch (DuplicateSupplierNameException ex)
        {
            ModelState.AddModelError(nameof(SupplierViewModel.Name), _l["A supplier named {0} already exists.", ex.Name]);
            return View(vm);
        }
        TempData["Flash"] = _l["Supplier {0} created.", supplier.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    // GET: /Suppliers/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var supplier = await _purchasing.GetSupplierAsync(id.Value);
        if (supplier is null) return NotFound();
        return View(SupplierViewModel.FromSupplier(supplier));
    }

    // POST: /Suppliers/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, SupplierViewModel vm)
    {
        if (id != vm.Id) return NotFound();
        if (!ModelState.IsValid) return View(vm);
        var supplier = vm.ToSupplier();
        try
        {
            await _purchasing.UpdateSupplierAsync(supplier);
        }
        catch (SupplierNotFoundException)
        {
            return NotFound();
        }
        catch (DuplicateSupplierNameException ex)
        {
            ModelState.AddModelError(nameof(SupplierViewModel.Name), _l["A supplier named {0} already exists.", ex.Name]);
            return View(vm);
        }
        TempData["Flash"] = _l["Supplier {0} saved.", supplier.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    // GET: /Suppliers/Delete/5
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var supplier = await _purchasing.GetSupplierAsync(id.Value);
        if (supplier is null) return NotFound();
        var orders = await _purchasing.GetOrdersAsync(null, id.Value, pageSize: 1);
        ViewBag.HasOrders = orders.TotalCount > 0;
        return View(supplier);
    }

    // POST: /Suppliers/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var supplier = await _purchasing.GetSupplierAsync(id);
        try
        {
            await _purchasing.DeleteSupplierAsync(id);
        }
        catch (SupplierHasOrdersException)
        {
            if (supplier is null) return RedirectToAction(nameof(Index));
            ModelState.AddModelError(string.Empty, _l["This supplier has purchase orders and cannot be deleted."]);
            ViewBag.HasOrders = true;
            return View("Delete", supplier);
        }
        if (supplier is not null)
            TempData["Flash"] = _l["Supplier {0} deleted.", supplier.Name].Value;
        return RedirectToAction(nameof(Index));
    }
}
