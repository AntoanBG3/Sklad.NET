using System.ComponentModel.DataAnnotations;
using Sklad.Models;

namespace Sklad.ViewModels;

public class RegisterMovementViewModel
{
    [Required]
    public int TireId { get; set; }

    [Required]
    [Display(Name = "Movement Type")]
    public MovementType MovementType { get; set; }

    // Nullable so the field starts blank instead of a prefilled 0, which is an
    // invalid value for In/Out anyway.
    [Required]
    [Range(0, int.MaxValue, ErrorMessage = "Quantity must be 0 or greater.")]
    public int? Quantity { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
