using System.ComponentModel.DataAnnotations;
using Sklad.Models;
using Sklad.Services;

namespace Sklad.ViewModels;

// Nullable so blank rows can be told apart from zeros and dropped before
// validation; [Required] still enforces presence on the rows that remain.
public class PurchaseOrderItemViewModel
{
    [Required]
    [Display(Name = "Tire")]
    public int? TireId { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int? Quantity { get; set; }

    [Required]
    [Range(0, 99999.99)]
    [Display(Name = "Unit Cost")]
    public decimal? UnitCost { get; set; }

    public bool IsBlank => TireId is null && Quantity is null && UnitCost is null;

    public PurchaseOrderLine ToLine() => new(TireId!.Value, Quantity!.Value, UnitCost!.Value);
}

public class PurchaseOrderFormViewModel
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Supplier")]
    public int? SupplierId { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public List<PurchaseOrderItemViewModel> Items { get; set; } = [];

    public static PurchaseOrderFormViewModel FromOrder(PurchaseOrder order) => new()
    {
        Id = order.Id,
        SupplierId = order.SupplierId,
        Note = order.Note,
        Items = order.Items
            .Select(i => new PurchaseOrderItemViewModel { TireId = i.TireId, Quantity = i.Quantity, UnitCost = i.UnitCost })
            .ToList()
    };

    public IReadOnlyList<PurchaseOrderLine> ToLines()
        => Items.Select(i => i.ToLine()).ToList();
}
