namespace Sklad.Services;

public class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }

    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public record WarehouseStats(int TotalSkus, int TotalUnits, int LowStockCount, decimal TotalValue);

public record ValueReportGroup(string Key, int Skus, int Units, decimal Value);

public record ValueReport(
    IReadOnlyList<ValueReportGroup> ByBrand,
    IReadOnlyList<ValueReportGroup> BySeason,
    decimal TotalValue);
