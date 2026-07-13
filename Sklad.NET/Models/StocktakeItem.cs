using System.ComponentModel.DataAnnotations;

namespace Sklad.Models;

public class StocktakeItem
{
    public int Id { get; set; }

    [Required]
    public int StocktakeId { get; set; }

    [Required]
    public int TireId { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    public int ExpectedQuantity { get; set; }

    [Required]
    public int ExpectedTireVersion { get; set; }

    [Range(0, int.MaxValue)]
    public int? CountedQuantity { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public Stocktake Stocktake { get; set; } = null!;
    public Tire Tire { get; set; } = null!;

    public long? Variance => CountedQuantity.HasValue
        ? (long)CountedQuantity.Value - ExpectedQuantity
        : null;
}
