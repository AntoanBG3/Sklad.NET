namespace Sklad.Services;

public interface IInventoryReportService
{
    Task<WarehouseStats> GetStatsAsync();

    Task<ValueReport> GetValueReportAsync();

    Task<MovementTrend> GetMovementTrendAsync(DateOnly from, DateOnly to);
}
