using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Sklad.Models;

public class Tire
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    [Display(Name = "SKU")]
    public string Sku { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Barcode { get; set; }

    [Required]
    [StringLength(100)]
    public string Brand { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Model { get; set; } = string.Empty;

    [Required]
    [Range(100, 400)]
    public int Width { get; set; }

    [Required]
    [Range(20, 90)]
    public int Profile { get; set; }

    [Required]
    [Range(10, 26)]
    public int Diameter { get; set; }

    [Required]
    public Season Season { get; set; }

    [Required]
    [Display(Name = "Type")]
    public TireType Type { get; set; }

    [Required]
    [Precision(18, 2)]
    [Range(0.01, 99999.99)]
    [Display(Name = "Unit Price")]
    public decimal UnitPrice { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    public int Quantity { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    [Display(Name = "Min Stock")]
    public int MinStock { get; set; }

    [StringLength(200)]
    public string? Location { get; set; }

    [NotMapped]
    public string Size => $"{Width}/{Profile} R{Diameter}";

    [NotMapped]
    public bool IsLowStock => Quantity <= MinStock;

    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
}
