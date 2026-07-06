using System.ComponentModel.DataAnnotations;
using Sklad.Models;

namespace Sklad.ViewModels;

public class SupplierViewModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Contact Person")]
    public string? ContactName { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [EmailAddress]
    [StringLength(200)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public static SupplierViewModel FromSupplier(Supplier s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        ContactName = s.ContactName,
        Phone = s.Phone,
        Email = s.Email,
        Notes = s.Notes
    };

    public Supplier ToSupplier() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        ContactName = string.IsNullOrWhiteSpace(ContactName) ? null : ContactName.Trim(),
        Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(),
        Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
    };
}
