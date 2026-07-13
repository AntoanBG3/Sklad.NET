using Sklad.Models;
using Sklad.ViewModels;

namespace Sklad.Services;

public interface IBarcodeLabelService
{
    TireLabelViewModel Create(Tire tire);
}

/// <summary>
/// Chooses the machine-readable identity for a label. A configured barcode is
/// preferred; SKU is a safe fallback because both resolve through Scan/FindByCode.
/// </summary>
public sealed class BarcodeLabelService(IBarcodeService barcodes) : IBarcodeLabelService
{
    public TireLabelViewModel Create(Tire tire)
    {
        var barcode = barcodes.Encode(tire.Barcode);
        var usesSku = barcode is null;
        barcode ??= barcodes.Encode(tire.Sku);

        return new TireLabelViewModel
        {
            Tire = tire,
            Code = barcode?.Value ?? tire.Barcode ?? tire.Sku,
            Barcode = barcode,
            UsesSkuFallback = usesSku && barcode is not null
        };
    }
}
