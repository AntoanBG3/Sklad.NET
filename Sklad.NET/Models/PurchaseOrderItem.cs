using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Sklad.Models;

public class PurchaseOrderItem
{
    public int Id { get; set; }

    [Required]
    public int PurchaseOrderId { get; set; }

    [Required]
    public int TireId { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    // 0 allowed: suppliers ship warranty replacements at no cost.
    [Required]
    [Precision(18, 2)]
    [Range(0, 99999.99)]
    [Display(Name = "Unit Cost")]
    public decimal UnitCost { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public Tire Tire { get; set; } = null!;
}
