using System.ComponentModel.DataAnnotations;

namespace Sklad.Models;

public class StockMovement
{
    public int Id { get; set; }

    [Required]
    public int TireId { get; set; }

    [Required]
    public MovementType MovementType { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Required]
    public DateTime Date { get; set; } = DateTime.UtcNow;

    [StringLength(500)]
    public string? Note { get; set; }

    public Tire Tire { get; set; } = null!;
}
