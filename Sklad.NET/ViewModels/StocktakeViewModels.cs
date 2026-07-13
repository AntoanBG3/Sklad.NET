using System.ComponentModel.DataAnnotations;
using Sklad.Models;
using Sklad.Services;

namespace Sklad.ViewModels;

public sealed class CreateStocktakeViewModel
{
    [StringLength(200)]
    [Display(Name = "Location")]
    public string? Location { get; set; }

    [StringLength(500)]
    [Display(Name = "Note")]
    public string? Note { get; set; }
}

public sealed class StocktakeCountLineViewModel
{
    public int Id { get; init; }
    public int TireId { get; init; }
    public required string Sku { get; init; }
    public required string Description { get; init; }
    public required string Size { get; init; }
    public string? Location { get; init; }
    public int ExpectedQuantity { get; init; }

    [Range(0, int.MaxValue)]
    [Display(Name = "Counted Quantity")]
    public int? CountedQuantity { get; set; }

    [StringLength(500)]
    [Display(Name = "Note")]
    public string? Note { get; set; }

    public long? Variance => CountedQuantity.HasValue
        ? (long)CountedQuantity.Value - ExpectedQuantity
        : null;

    public StocktakeCount ToCount() => new(Id, CountedQuantity, Note);
}

public sealed class StocktakeCountViewModel
{
    public int Id { get; init; }
    public required string Number { get; init; }
    public StocktakeStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Location { get; init; }
    public string? Note { get; init; }
    public string? CreatedBy { get; init; }
    public string? CompletedBy { get; init; }

    [Required]
    [Display(Name = "Version token")]
    public int? Version { get; init; }

    public List<StocktakeCountLineViewModel> Items { get; init; } = [];

    public bool IsOpen => Status == StocktakeStatus.Draft;
    public int CountedItems => Items.Count(item => item.CountedQuantity.HasValue);
    public int RemainingItems => Items.Count - CountedItems;
    public int VarianceItems => Items.Count(item => item.Variance is not null and not 0);
    public long NetVariance => Items.Sum(item => item.Variance ?? 0);

    public static StocktakeCountViewModel FromStocktake(Stocktake stocktake) => new()
    {
        Id = stocktake.Id,
        Number = stocktake.Number,
        Status = stocktake.Status,
        CreatedAt = stocktake.CreatedAt,
        CompletedAt = stocktake.CompletedAt,
        Location = stocktake.Location,
        Note = stocktake.Note,
        CreatedBy = stocktake.CreatedBy,
        CompletedBy = stocktake.CompletedBy,
        Version = stocktake.Version,
        Items = stocktake.Items
            .OrderBy(item => item.Tire.Location)
            .ThenBy(item => item.Tire.Brand)
            .ThenBy(item => item.Tire.Model)
            .ThenBy(item => item.Tire.Sku)
            .Select(item => new StocktakeCountLineViewModel
            {
                Id = item.Id,
                TireId = item.TireId,
                Sku = item.Tire.Sku,
                Description = $"{item.Tire.Brand} {item.Tire.Model}",
                Size = item.Tire.Size,
                Location = item.Tire.Location,
                ExpectedQuantity = item.ExpectedQuantity,
                CountedQuantity = item.CountedQuantity,
                Note = item.Note
            })
            .ToList()
    };
}

public sealed class SaveStocktakeCountLineViewModel
{
    public int Id { get; set; }

    [Range(0, int.MaxValue)]
    [Display(Name = "Counted Quantity")]
    public int? CountedQuantity { get; set; }

    [StringLength(500)]
    [Display(Name = "Note")]
    public string? Note { get; set; }

    public StocktakeCount ToCount() => new(Id, CountedQuantity, Note);
}

/// <summary>
/// Narrow POST contract: display-only tire fields never participate in model
/// binding or implicit required validation.
/// </summary>
public sealed class SaveStocktakeCountsViewModel
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Version token")]
    public int? Version { get; set; }

    public List<SaveStocktakeCountLineViewModel> Items { get; set; } = [];
}

public sealed class StocktakePrintViewModel
{
    public required StocktakeCountViewModel Count { get; init; }
    public required ShopSettings Shop { get; init; }
}
