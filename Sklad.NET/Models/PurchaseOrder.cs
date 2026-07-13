using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sklad.Models;

public class PurchaseOrder
{
    public int Id { get; set; }

    [Required]
    public int SupplierId { get; set; }

    [Required]
    public PurchaseOrderStatus Status { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? OrderedAt { get; set; }

    public DateTime? ReceivedAt { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    [StringLength(100)]
    public string? CreatedBy { get; set; }

    [StringLength(100)]
    public string? ReceivedBy { get; set; }

    // Every mutation of the order aggregate (lines or lifecycle state) advances
    // this token. That makes the status check and the subsequent write one
    // optimistic-concurrency operation instead of allowing a stale request to
    // overwrite a concurrent edit, cancellation, or receipt.
    public int Version { get; set; }

    public Supplier Supplier { get; set; } = null!;

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();

    [NotMapped]
    public string Number => $"PO-{Id:D4}";

    [NotMapped]
    public decimal Total => Items.Sum(i => i.Quantity * i.UnitCost);

    [NotMapped]
    public long TotalUnits => Items.Sum(i => (long)i.Quantity);

    [NotMapped]
    public bool IsEditable => Status == PurchaseOrderStatus.Draft;

    [NotMapped]
    public bool IsOpen => Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered;
}
