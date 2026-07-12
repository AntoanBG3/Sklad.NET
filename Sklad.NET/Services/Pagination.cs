namespace Sklad.Services;

internal static class Pagination
{
    public static int ClampPage(int requestedPage, int totalCount, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        return Math.Clamp(requestedPage, 1, totalPages);
    }
}
