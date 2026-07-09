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

    public static ShopSettingsViewModel FromSettings(ShopSettings s) => new()
    {
        Name = s.Name,
        Address = s.Address,
        VatNumber = s.VatNumber,
        Phone = s.Phone,
        Email = s.Email
    };

    public ShopSettings ToSettings() => new()
    {
        Id = ShopSettings.SingletonId,
        Name = Blank(Name),
        Address = Blank(Address),
        VatNumber = Blank(VatNumber),
        Phone = Blank(Phone),
        Email = Blank(Email)
    };

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
