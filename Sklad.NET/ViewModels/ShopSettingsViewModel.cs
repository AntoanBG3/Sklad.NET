using System.ComponentModel.DataAnnotations;
using Sklad.Models;

namespace Sklad.ViewModels;

public class ShopSettingsViewModel
{
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

    public static ShopSettingsViewModel FromSettings(ShopSettings s) => new()
    {
        Name = s.Name,
        Address = s.Address,
        VatNumber = s.VatNumber,
        Phone = s.Phone,
        Email = s.Email,
        DefaultMinStock = s.DefaultMinStock,
        PageSize = s.PageSize,
        DefaultCulture = s.DefaultCulture,
        ReportRangeMonths = s.ReportRangeMonths
    };

    public ShopSettings ToSettings() => new()
    {
        Id = ShopSettings.SingletonId,
        Name = Blank(Name),
        Address = Blank(Address),
        VatNumber = Blank(VatNumber),
        Phone = Blank(Phone),
        Email = Blank(Email),
        DefaultMinStock = DefaultMinStock,
        PageSize = PageSize,
        DefaultCulture = Blank(DefaultCulture),
        ReportRangeMonths = ReportRangeMonths
    };

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
