using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sklad.Models;

public class Stocktake
{
    public int Id { get; set; }

    [Required]
    public StocktakeStatus Status { get; set; } = StocktakeStatus.Draft;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    [StringLength(200)]
    public string? Location { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    [StringLength(100)]
    public string? CreatedBy { get; set; }

    [StringLength(100)]
    public string? CompletedBy { get; set; }

    public int Version { get; set; }

    public ICollection<StocktakeItem> Items { get; set; } = new List<StocktakeItem>();

    [NotMapped]
    public string Number => $"ST-{Id:D4}";

    [NotMapped]
    public bool IsOpen => Status == StocktakeStatus.Draft;

    [NotMapped]
    public int CountedItems => Items.Count(item => item.CountedQuantity.HasValue);

    [NotMapped]
    public int VarianceItems => Items.Count(item =>
        item.CountedQuantity.HasValue && item.CountedQuantity != item.ExpectedQuantity);

    [NotMapped]
    public long NetVariance => Items
        .Where(item => item.CountedQuantity.HasValue)
        .Sum(item => (long)item.CountedQuantity!.Value - item.ExpectedQuantity);
}
