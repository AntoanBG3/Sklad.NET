using System.ComponentModel.DataAnnotations;

namespace Sklad.Models;

public class ShopSettings
{
    // Singleton row; the service reads and upserts this id.
    public const int SingletonId = 1;

    public int Id { get; set; }

    [StringLength(200)]
    [Display(Name = "Shop Name")]
    public string? Name { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(50)]
    [Display(Name = "VAT / EIK")]
    public string? VatNumber { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [EmailAddress]
    [StringLength(200)]
    public string? Email { get; set; }
}
