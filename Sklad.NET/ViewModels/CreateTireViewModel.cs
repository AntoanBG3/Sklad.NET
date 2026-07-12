using System.ComponentModel.DataAnnotations;
using Sklad.Models;

namespace Sklad.ViewModels;

// Nullable numerics so the Create form starts blank (a bound Tire would render
// its int defaults as literal zeros in every field); [Required] still enforces
// presence and ToTire() runs only after validation.
public class CreateTireViewModel
{
    [Required]
    [StringLength(50)]
    [Display(Name = "SKU")]
    public string Sku { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Barcode")]
    public string? Barcode { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "Brand")]
    public string Brand { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [Display(Name = "Model")]
    public string Model { get; set; } = string.Empty;

    [Required]
    [Range(100, 400)]
    [Display(Name = "Width")]
    public int? Width { get; set; }

    [Required]
    [Range(20, 90)]
    [Display(Name = "Profile")]
    public int? Profile { get; set; }

    [Required]
    [Range(10, 26)]
    [Display(Name = "Diameter")]
    public int? Diameter { get; set; }

    [Required]
    [Display(Name = "Season")]
    public Season Season { get; set; }

    [Required]
    [Display(Name = "Type")]
    public TireType Type { get; set; }

    [Required]
    [Range(0.01, 99999.99)]
    [Display(Name = "Unit Price")]
    public decimal? UnitPrice { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    [Display(Name = "Quantity")]
    public int? Quantity { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    [Display(Name = "Min Stock")]
    public int? MinStock { get; set; }

    [StringLength(200)]
    [Display(Name = "Location")]
    public string? Location { get; set; }

    public Tire ToTire() => new()
    {
        Sku = Sku.Trim(),
        Barcode = string.IsNullOrWhiteSpace(Barcode) ? null : Barcode.Trim(),
        Brand = Brand.Trim(),
        Model = Model.Trim(),
        Width = Width!.Value,
        Profile = Profile!.Value,
        Diameter = Diameter!.Value,
        Season = Season,
        Type = Type,
        UnitPrice = UnitPrice!.Value,
        Quantity = Quantity!.Value,
        MinStock = MinStock!.Value,
        Location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim()
    };
}
