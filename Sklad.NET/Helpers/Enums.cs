using Sklad.Models;

namespace Sklad.Helpers;

public static class Enums
{
    public static string Key(Season s) => s == Season.AllSeason ? "All-Season" : s.ToString();
    public static string Key(TireType t) => t.ToString();
    public static string Key(MovementType m) => m.ToString();
}
