using Sklad.Models;

namespace Sklad.Services;

// TODO: Implement InventoryService and register it in Program.cs as scoped

public interface IInventoryService
{
    // TODO: Search — returns filtered tires matching any combination of sku/brand/model/width/profile/diameter/season/type
    Task<IEnumerable<Tire>> SearchAsync(string? sku, string? brand, string? model,
        int? width, int? profile, int? diameter, Season? season, TireType? type);

    // TODO: Low-stock report — returns tires where Quantity <= MinStock
    Task<IEnumerable<Tire>> GetLowStockAsync();

    // TODO: Register movement — creates a StockMovement and adjusts Tire.Quantity
    //   In: adds quantity, Out: subtracts (reject if would go negative), Adjustment: sets directly
    Task RegisterMovementAsync(int tireId, MovementType movementType, int quantity, string? note);

    // TODO: Export — returns all (or filtered) tires as CSV bytes for download
    Task<byte[]> ExportCsvAsync(IEnumerable<Tire> tires);
}
