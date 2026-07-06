using System.ComponentModel.DataAnnotations;

namespace Sklad.Models;

public class AppUser
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    public UserRole Role { get; set; }

    // Carried in the auth cookie and rotated on password/role changes so stale
    // sessions are rejected instead of living out their 12-hour lifetime.
    [Required]
    [StringLength(50)]
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
