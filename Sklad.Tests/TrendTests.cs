using System.Globalization;
using Sklad.Helpers;

namespace Sklad.Tests;

public class TrendTests
{
    [Theory]
    [InlineData(60, TrendGranularity.Day)]
    [InlineData(61, TrendGranularity.Month)]
    [InlineData(0, TrendGranularity.Day)]
    [InlineData(365, TrendGranularity.Month)]
    public void Granularity_switches_to_months_past_sixty_days(int spanDays, TrendGranularity expected)
    {
        var from = new DateOnly(2026, 1, 1);
        Assert.Equal(expected, Trend.Granularity(from, from.AddDays(spanDays)));
    }

    [Fact]
    public void BucketStart_truncates_to_the_first_of_the_month()
    {
        Assert.Equal(new DateOnly(2026, 3, 1),
            Trend.BucketStart(new DateOnly(2026, 3, 17), TrendGranularity.Month));
    }

    [Fact]
    public void BucketStart_keeps_the_day_when_daily()
    {
        Assert.Equal(new DateOnly(2026, 3, 17),
            Trend.BucketStart(new DateOnly(2026, 3, 17), TrendGranularity.Day));
    }

    [Fact]
    public void Sequence_covers_every_day_inclusive()
    {
        var days = Trend.Sequence(new DateOnly(2026, 1, 30), new DateOnly(2026, 2, 2), TrendGranularity.Day).ToList();
        Assert.Equal(4, days.Count);
        Assert.Equal(new DateOnly(2026, 1, 30), days[0]);
        Assert.Equal(new DateOnly(2026, 2, 2), days[^1]);
    }

    [Fact]
    public void Sequence_covers_every_month_inclusive_from_partial_months()
    {
        var months = Trend.Sequence(new DateOnly(2026, 1, 17), new DateOnly(2026, 4, 3), TrendGranularity.Month).ToList();
        Assert.Equal(4, months.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), months[0]);
        Assert.Equal(new DateOnly(2026, 4, 1), months[^1]);
    }

    [Fact]
    public void Sequence_of_a_single_day_yields_one_bucket()
    {
        var day = new DateOnly(2026, 5, 5);
        Assert.Single(Trend.Sequence(day, day, TrendGranularity.Day));
    }

    [Fact]
    public void Label_follows_the_current_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            Assert.Equal("Mar 2026", Trend.Label(new DateOnly(2026, 3, 1), TrendGranularity.Month));

            CultureInfo.CurrentCulture = new CultureInfo("bg-BG");
            Assert.DoesNotContain("Mar", Trend.Label(new DateOnly(2026, 3, 1), TrendGranularity.Month));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Label_omits_the_year_when_daily()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            var label = Trend.Label(new DateOnly(2026, 3, 17), TrendGranularity.Day);
            Assert.Equal("17 Mar", label);
            Assert.DoesNotContain("2026", label);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
