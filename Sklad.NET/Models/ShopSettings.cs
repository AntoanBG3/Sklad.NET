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

    // Shop-wide preferences; null means "use the built-in default" so an
    // unconfigured install behaves exactly as before the columns existed.
    [Range(0, int.MaxValue)]
    [Display(Name = "Default Min Stock")]
    public int? DefaultMinStock { get; set; }

    [Range(10, 200)]
    [Display(Name = "Items per Page")]
    public int? PageSize { get; set; }

    [StringLength(10)]
    [Display(Name = "Default Language")]
    public string? DefaultCulture { get; set; }

    [Range(1, 120)]
    [Display(Name = "Report Range (Months)")]
    public int? ReportRangeMonths { get; set; }
}
