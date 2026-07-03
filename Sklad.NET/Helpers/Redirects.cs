namespace Sklad.Helpers;

public static class Redirects
{
    /// <summary>
    /// Same rules as Url.IsLocalUrl, usable without an IUrlHelper. LocalRedirect
    /// throws (500) on non-local URLs instead of falling back, so callers must
    /// sanitize first.
    /// </summary>
    public static string Safe(string? returnUrl)
        => IsLocal(returnUrl) ? returnUrl! : "/";

    private static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (url[0] == '/')
            return url.Length == 1 || (url[1] != '/' && url[1] != '\\');
        if (url.Length > 1 && url[0] == '~' && url[1] == '/')
            return true;
        return false;
    }
}
