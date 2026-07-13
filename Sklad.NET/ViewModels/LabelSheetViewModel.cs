using Sklad.Models;
using Sklad.Services;

namespace Sklad.ViewModels;

public sealed class TireLabelViewModel
{
    public required Tire Tire { get; init; }
    public required string Code { get; init; }
    public BarcodeSymbol? Barcode { get; init; }
    public bool UsesSkuFallback { get; init; }
}

public sealed class LabelSheetViewModel
{
    public required IReadOnlyList<TireLabelViewModel> Labels { get; init; }
    public required ShopSettings Shop { get; init; }
    public required TireFilterViewModel Filter { get; init; }
    public required int Copies { get; init; }
    public required int MatchedTires { get; init; }
    public bool IsTruncated { get; init; }
    public int? SourceTireId { get; init; }
    public bool IsSingleTire => SourceTireId.HasValue;
}
