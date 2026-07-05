using Sklad.Models;

namespace Sklad.ViewModels;

public class TireFilterViewModel
{
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public int? Width { get; set; }
    public int? Profile { get; set; }
    public int? Diameter { get; set; }
    public Season? Season { get; set; }
    public TireType? Type { get; set; }
    public string? Location { get; set; }
    public bool? LowOnly { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;

    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(Sku) ||
        !string.IsNullOrWhiteSpace(Barcode) ||
        !string.IsNullOrWhiteSpace(Brand) ||
        !string.IsNullOrWhiteSpace(Model) ||
        Width.HasValue || Profile.HasValue || Diameter.HasValue ||
        Season.HasValue || Type.HasValue ||
        !string.IsNullOrWhiteSpace(Location) || LowOnly == true;
}
