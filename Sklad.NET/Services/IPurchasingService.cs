using Sklad.Models;

namespace Sklad.Services;

public record PurchaseOrderLine(int TireId, int Quantity, decimal UnitCost);

public record SupplierSummary(Supplier Supplier, int OpenOrders, int TotalOrders);

public interface IPurchasingService
{
    Task<IReadOnlyList<SupplierSummary>> GetSuppliersAsync();

    Task<Supplier?> GetSupplierAsync(int id);

    Task CreateSupplierAsync(Supplier supplier);

    Task UpdateSupplierAsync(Supplier supplier);

    Task DeleteSupplierAsync(int id);

    Task<PagedResult<PurchaseOrder>> GetOrdersAsync(PurchaseOrderStatus? status, int? supplierId = null, int page = 1, int pageSize = InventoryService.DefaultPageSize);

    Task<PurchaseOrder?> GetOrderAsync(int id);

    Task<PurchaseOrder> CreateOrderAsync(int supplierId, string? note, IReadOnlyList<PurchaseOrderLine> lines, string? userName = null);

    Task UpdateDraftAsync(int id, int expectedVersion, int supplierId, string? note, IReadOnlyList<PurchaseOrderLine> lines);

    Task MarkOrderedAsync(int id, int expectedVersion);

    Task ReceiveAsync(int id, int expectedVersion, string? userName = null);

    Task CancelAsync(int id, int expectedVersion);
}
