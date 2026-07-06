using Sklad.Models;

namespace Sklad.Services;

public interface IUserService
{
    Task<AppUser?> ValidateCredentialsAsync(string username, string password);

    Task<bool> IsSecurityStampValidAsync(int userId, string securityStamp);

    Task<IReadOnlyList<AppUser>> GetUsersAsync();

    Task<AppUser?> GetUserAsync(int id);

    Task<AppUser> CreateUserAsync(string username, string password, UserRole role);

    Task UpdateUserAsync(int id, UserRole role, string? newPassword);

    Task DeleteUserAsync(int id, int actingUserId);
}
