using Sklad.Models;

namespace Sklad.ViewModels;

public class PurchaseOrderPrintViewModel
{
    public required PurchaseOrder Order { get; init; }

    public required ShopSettings Shop { get; init; }

    public bool HasLetterhead =>
        !string.IsNullOrWhiteSpace(Shop.Name)
        || !string.IsNullOrWhiteSpace(Shop.Address)
        || !string.IsNullOrWhiteSpace(Shop.VatNumber)
        || !string.IsNullOrWhiteSpace(Shop.Phone)
        || !string.IsNullOrWhiteSpace(Shop.Email);
}
