namespace Sklad.Models;

// User first so an uninitialized role never defaults to Admin.
public enum UserRole
{
    User,
    Admin
}
