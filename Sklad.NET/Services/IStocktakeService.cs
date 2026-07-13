using Sklad.Models;

namespace Sklad.Services;

public readonly record struct StocktakeCount(int ItemId, int? CountedQuantity, string? Note);

public interface IStocktakeService
{
    Task<PagedResult<Stocktake>> GetStocktakesAsync(
        StocktakeStatus? status = null,
        int page = 1,
        int pageSize = InventoryService.DefaultPageSize);

    Task<Stocktake?> GetStocktakeAsync(int id);

    Task<Stocktake> CreateAsync(string? location, string? note, string? userName = null);

    Task SaveCountsAsync(int id, int expectedVersion, IReadOnlyList<StocktakeCount> counts);

    Task<int> CompleteAsync(int id, int expectedVersion, string? userName = null);

    Task<int> RefreshChangedAsync(int id, int expectedVersion);

    Task CancelAsync(int id, int expectedVersion);
}
