using System.Globalization;

namespace Sklad.Helpers;

public static class Money
{
    public const decimal BgnPerEur = 1.95583m;

    public static string Euro(decimal eur, int decimals = 2)
    {
        var f = "N" + decimals.ToString(CultureInfo.InvariantCulture);
        return $"{eur.ToString(f, CultureInfo.CurrentCulture)} €";
    }

    public static string Lev(decimal eur, int decimals = 2)
    {
        var bgn = Math.Round(eur * BgnPerEur, 2, MidpointRounding.AwayFromZero);
        var f = "N" + decimals.ToString(CultureInfo.InvariantCulture);
        return $"{bgn.ToString(f, CultureInfo.CurrentCulture)} лв.";
    }

    public static string Dual(decimal eur, int decimals = 2) => $"{Euro(eur, decimals)} ({Lev(eur, decimals)})";
}
