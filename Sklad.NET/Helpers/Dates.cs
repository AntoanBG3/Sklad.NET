namespace Sklad.Helpers;

public static class Dates
{
    // Fixed shop timezone: ToLocalTime() would follow the host, and a UTC
    // container would silently shift every timestamp for Bulgarian users.
    private static readonly TimeZoneInfo Sofia = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time"); }
    }

    public static DateTime Shop(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Sofia);

    public static string Stamp(DateTime utc)
        => Shop(utc).ToString("dd MMM yyyy HH:mm");

    // Movements are stored UTC but users filter by shop-local calendar days.
    public static DateTime StartOfDayUtc(DateOnly day)
        => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), Sofia);
}
