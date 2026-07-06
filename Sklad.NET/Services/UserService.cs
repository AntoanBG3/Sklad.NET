using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sklad.Data;
using Sklad.Models;

namespace Sklad.Services;

public class UserService : IUserService
{
    private static readonly PasswordHasher<AppUser> Hasher = new();

    // Verified against when the username doesn't exist, so a login attempt costs
    // the same either way and usernames can't be probed by timing.
    private static readonly string DummyHash = Hasher.HashPassword(new AppUser(), Guid.NewGuid().ToString("N"));

    private readonly SkladDbContext _db;
    private readonly ILogger<UserService> _logger;

    public UserService(SkladDbContext db, ILogger<UserService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AppUser?> ValidateCredentialsAsync(string username, string password)
    {
        var name = username.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == name);
        if (user is null)
        {
            Hasher.VerifyHashedPassword(new AppUser(), DummyHash, password);
            return null;
        }

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
            await _db.SaveChangesAsync();
        }
        return user;
    }

    public Task<bool> IsSecurityStampValidAsync(int userId, string securityStamp)
        => _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.SecurityStamp == securityStamp);

    public async Task<IReadOnlyList<AppUser>> GetUsersAsync()
        => await _db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync();

    public Task<AppUser?> GetUserAsync(int id)
        => _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);

    public async Task<AppUser> CreateUserAsync(string username, string password, UserRole role)
    {
        var name = username.Trim();
        if (await _db.Users.AnyAsync(u => u.Username == name))
            throw new DuplicateUsernameException(name);

        var user = new AppUser { Username = name, Role = role };
        user.PasswordHash = Hasher.HashPassword(user, password);
        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueUsernameViolation(ex))
        {
            throw new DuplicateUsernameException(name);
        }
        _logger.LogInformation("User {Username} created with role {Role}", name, role);
        return user;
    }

    public async Task UpdateUserAsync(int id, UserRole role, string? newPassword)
    {
        var user = await _db.Users.FindAsync(id)
            ?? throw new UserNotFoundException();

        if (user.Role == UserRole.Admin && role != UserRole.Admin && await IsLastAdminAsync(id))
            throw new LastAdminException();

        var stampNeedsRotation = user.Role != role || !string.IsNullOrEmpty(newPassword);
        user.Role = role;
        if (!string.IsNullOrEmpty(newPassword))
            user.PasswordHash = Hasher.HashPassword(user, newPassword);
        // Rotating the stamp cuts off existing sessions, so a password reset or
        // role change takes effect immediately instead of at cookie expiry.
        if (stampNeedsRotation)
            user.SecurityStamp = Guid.NewGuid().ToString("N");

        await _db.SaveChangesAsync();
        _logger.LogInformation("User {Username} updated: role {Role}, password changed: {PasswordChanged}",
            user.Username, role, !string.IsNullOrEmpty(newPassword));
    }

    public async Task DeleteUserAsync(int id, int actingUserId)
    {
        if (id == actingUserId)
            throw new CannotDeleteSelfException();

        var user = await _db.Users.FindAsync(id)
            ?? throw new UserNotFoundException();

        if (user.Role == UserRole.Admin && await IsLastAdminAsync(id))
            throw new LastAdminException();

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        _logger.LogInformation("User {Username} (id {Id}) deleted", user.Username, id);
    }

    private async Task<bool> IsLastAdminAsync(int adminId)
        => !await _db.Users.AnyAsync(u => u.Role == UserRole.Admin && u.Id != adminId);

    private static bool IsUniqueUsernameViolation(DbUpdateException ex)
        => ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } inner
           && inner.Message.Contains("Username");
}
