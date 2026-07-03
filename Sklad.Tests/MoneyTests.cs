using System.Globalization;
using Sklad.Helpers;

namespace Sklad.Tests;

public class MoneyTests
{
    private static void WithCulture(string culture, Action assert)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            assert();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Lev_converts_at_fixed_rate_with_away_from_zero_rounding()
    {
        WithCulture("en-GB", () =>
        {
            Assert.Equal("195.58 лв.", Money.Lev(100m));
            Assert.Equal("1.96 лв.", Money.Lev(1m));
        });
    }

    [Fact]
    public void Euro_formats_with_current_culture_separators()
    {
        WithCulture("en-GB", () => Assert.Equal("1,234.50 €", Money.Euro(1234.5m)));
        WithCulture("bg-BG", () =>
        {
            var text = Money.Euro(1234.5m);
            Assert.Contains("234,50", text);
            Assert.EndsWith("€", text);
        });
    }

    [Fact]
    public void Dual_shows_euro_then_lev()
    {
        WithCulture("en-GB", () => Assert.Equal("100.00 € (195.58 лв.)", Money.Dual(100m)));
    }

    [Fact]
    public void Zero_decimals_rounds_display()
    {
        WithCulture("en-GB", () =>
        {
            Assert.Equal("1,235 €", Money.Euro(1234.5m, 0));
            Assert.Equal("196 лв.", Money.Lev(100m, 0));
        });
    }
}
