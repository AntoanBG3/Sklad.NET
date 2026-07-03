using Sklad.Models;
using Sklad.ViewModels;

namespace Sklad.Services;

public interface IInventoryService
{
    Task<PagedResult<Tire>> SearchAsync(TireFilterViewModel filter, int pageSize = InventoryService.DefaultPageSize);

    Task<IEnumerable<Tire>> GetLowStockAsync();

    Task<Tire?> GetTireAsync(int id, bool includeMovements = false);

    Task<Tire?> FindByCodeAsync(string code);

    Task CreateTireAsync(Tire tire, string? userName = null);

    Task UpdateTireAsync(Tire tire);

    Task DeleteTireAsync(int id);

    Task<WarehouseStats> GetStatsAsync();

    Task<int> RegisterMovementAsync(int tireId, MovementType movementType, int quantity, string? note, string? userName = null);

    Task<PagedResult<StockMovement>> GetMovementsAsync(MovementType? type, int page, int pageSize = InventoryService.DefaultPageSize);

    Task<ValueReport> GetValueReportAsync();

    Task<byte[]> ExportCsvAsync(IEnumerable<Tire> tires);
}
