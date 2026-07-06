namespace Sklad.Services;

public abstract class UserException : Exception
{
    protected UserException(string message) : base(message) { }
}

public sealed class UserNotFoundException : UserException
{
    public UserNotFoundException() : base("User not found.") { }
}

public sealed class DuplicateUsernameException : UserException
{
    public string Username { get; }

    public DuplicateUsernameException(string username) : base($"A user named '{username}' already exists.")
        => Username = username;
}

public sealed class LastAdminException : UserException
{
    public LastAdminException() : base("The last administrator cannot be removed or demoted.") { }
}

public sealed class CannotDeleteSelfException : UserException
{
    public CannotDeleteSelfException() : base("You cannot delete your own account.") { }
}
