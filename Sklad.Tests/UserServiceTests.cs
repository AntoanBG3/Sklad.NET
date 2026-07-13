using Microsoft.Extensions.Logging.Abstractions;
using Sklad.Data;
using Sklad.Models;
using Sklad.Services;

namespace Sklad.Tests;

public class UserServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static UserService CreateService(SkladDbContext context)
        => new(context, NullLogger<UserService>.Instance);

    [Fact]
    public async Task Created_user_can_sign_in_with_the_right_password_only()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        await service.CreateUserAsync("maria", "s3cret-pass", UserRole.User);

        var ok = await service.ValidateCredentialsAsync("maria", "s3cret-pass");
        var wrong = await service.ValidateCredentialsAsync("maria", "not-it");
        var unknown = await service.ValidateCredentialsAsync("nobody", "s3cret-pass");

        Assert.NotNull(ok);
        Assert.Equal(UserRole.User, ok!.Role);
        Assert.Null(wrong);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Password_is_stored_hashed_not_plaintext()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var user = await service.CreateUserAsync("maria", "s3cret-pass", UserRole.User);

        Assert.NotEqual("s3cret-pass", user.PasswordHash);
        Assert.DoesNotContain("s3cret-pass", user.PasswordHash);
    }

    [Fact]
    public async Task Duplicate_username_is_rejected_case_insensitively()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        await service.CreateUserAsync("Admin", "password-1", UserRole.Admin);

        await Assert.ThrowsAsync<DuplicateUsernameException>(
            () => service.CreateUserAsync("admin", "password-2", UserRole.User));
    }

    [Fact]
    public async Task Duplicate_cyrillic_username_is_rejected_case_insensitively()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        await service.CreateUserAsync("Админ", "password-1", UserRole.Admin);

        await Assert.ThrowsAsync<DuplicateUsernameException>(
            () => service.CreateUserAsync("аДМИН", "password-2", UserRole.User));
    }

    [Fact]
    public async Task Username_lookup_at_sign_in_is_case_insensitive()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        await service.CreateUserAsync("Admin", "password-1", UserRole.Admin);

        Assert.NotNull(await service.ValidateCredentialsAsync("admin", "password-1"));
    }

    [Fact]
    public async Task Cyrillic_username_lookup_at_sign_in_is_case_insensitive()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        await service.CreateUserAsync("Админ", "password-1", UserRole.Admin);

        Assert.NotNull(await service.ValidateCredentialsAsync("аДМИН", "password-1"));
    }

    [Fact]
    public async Task Password_change_rotates_the_security_stamp()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var user = await service.CreateUserAsync("maria", "s3cret-pass", UserRole.User);
        var oldStamp = user.SecurityStamp;

        await service.UpdateUserAsync(user.Id, UserRole.User, "another-pass");

        Assert.False(await service.IsSecurityStampValidAsync(user.Id, oldStamp));
        Assert.NotNull(await service.ValidateCredentialsAsync("maria", "another-pass"));
    }

    [Fact]
    public async Task Role_change_rotates_the_security_stamp()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        await service.CreateUserAsync("boss", "password-1", UserRole.Admin);
        var user = await service.CreateUserAsync("maria", "s3cret-pass", UserRole.User);
        var oldStamp = user.SecurityStamp;

        await service.UpdateUserAsync(user.Id, UserRole.Admin, null);

        Assert.False(await service.IsSecurityStampValidAsync(user.Id, oldStamp));
    }

    [Fact]
    public async Task Update_without_changes_keeps_the_stamp_valid()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var user = await service.CreateUserAsync("maria", "s3cret-pass", UserRole.User);

        await service.UpdateUserAsync(user.Id, UserRole.User, null);

        Assert.True(await service.IsSecurityStampValidAsync(user.Id, user.SecurityStamp));
    }

    [Fact]
    public async Task Last_admin_cannot_be_demoted()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var admin = await service.CreateUserAsync("boss", "password-1", UserRole.Admin);

        await Assert.ThrowsAsync<LastAdminException>(
            () => service.UpdateUserAsync(admin.Id, UserRole.User, null));
    }

    [Fact]
    public async Task Admin_can_be_demoted_when_another_admin_exists()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var admin = await service.CreateUserAsync("boss", "password-1", UserRole.Admin);
        await service.CreateUserAsync("boss2", "password-2", UserRole.Admin);

        await service.UpdateUserAsync(admin.Id, UserRole.User, null);

        Assert.Equal(UserRole.User, (await service.GetUserAsync(admin.Id))!.Role);
    }

    [Fact]
    public async Task Last_admin_cannot_be_deleted()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var admin = await service.CreateUserAsync("boss", "password-1", UserRole.Admin);
        var other = await service.CreateUserAsync("maria", "password-2", UserRole.User);

        await Assert.ThrowsAsync<LastAdminException>(
            () => service.DeleteUserAsync(admin.Id, other.Id));
    }

    [Fact]
    public async Task Users_cannot_delete_themselves()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var admin = await service.CreateUserAsync("boss", "password-1", UserRole.Admin);

        await Assert.ThrowsAsync<CannotDeleteSelfException>(
            () => service.DeleteUserAsync(admin.Id, admin.Id));
    }

    [Fact]
    public async Task Deleting_a_user_invalidates_their_stamp()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var admin = await service.CreateUserAsync("boss", "password-1", UserRole.Admin);
        var user = await service.CreateUserAsync("maria", "password-2", UserRole.User);

        await service.DeleteUserAsync(user.Id, admin.Id);

        Assert.False(await service.IsSecurityStampValidAsync(user.Id, user.SecurityStamp));
        Assert.Null(await service.GetUserAsync(user.Id));
    }
}
