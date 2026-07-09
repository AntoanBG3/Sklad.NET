namespace Sklad.Helpers;

public enum TrendGranularity
{
    Day,
    Month
}

public static class Trend
{
    public const int DailyMaxSpanDays = 60;

    public static TrendGranularity Granularity(DateOnly from, DateOnly to) =>
        to.DayNumber - from.DayNumber <= DailyMaxSpanDays ? TrendGranularity.Day : TrendGranularity.Month;

    public static DateOnly BucketStart(DateOnly day, TrendGranularity granularity) =>
        granularity == TrendGranularity.Day ? day : new DateOnly(day.Year, day.Month, 1);

    public static IEnumerable<DateOnly> Sequence(DateOnly from, DateOnly to, TrendGranularity granularity)
    {
        var cursor = BucketStart(from, granularity);
        var last = BucketStart(to, granularity);
        while (cursor <= last)
        {
            yield return cursor;
            cursor = granularity == TrendGranularity.Day ? cursor.AddDays(1) : cursor.AddMonths(1);
        }
    }

    public static string Label(DateOnly start, TrendGranularity granularity) =>
        granularity == TrendGranularity.Day ? start.ToString("d MMM") : start.ToString("MMM yyyy");
}
