namespace Sklad.Services;

public readonly record struct BarcodeBar(int X, int Width);

public sealed record BarcodeSymbol(
    string Value,
    IReadOnlyList<int> Codewords,
    IReadOnlyList<BarcodeBar> Bars,
    int ModuleCount);

public interface IBarcodeService
{
    BarcodeSymbol? Encode(string? value);
}

/// <summary>
/// Encodes printable ASCII as Code 128 set B. The returned geometry is made of
/// integer-width modules, which lets Razor render a crisp SVG at any printer DPI.
/// </summary>
public sealed class Code128BarcodeService : IBarcodeService
{
    private const int StartB = 104;
    private const int Stop = 106;
    private const int QuietZone = 10;

    // ISO/IEC 15417 bar/space widths for symbols 0..106. Every symbol starts
    // with a bar; 0..105 are 11 modules and the stop symbol is 13 modules.
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222",
        "122213", "122312", "132212", "221213", "221312", "231212",
        "112232", "122132", "122231", "113222", "123122", "123221",
        "223211", "221132", "221231", "213212", "223112", "312131",
        "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321",
        "112313", "132113", "132311", "211313", "231113", "231311",
        "112133", "112331", "132131", "113123", "113321", "133121",
        "313121", "211331", "231131", "213113", "213311", "213131",
        "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124",
        "121421", "141122", "141221", "112214", "112412", "122114",
        "122411", "142112", "142211", "241211", "221114", "413111",
        "241112", "134111", "111242", "121142", "121241", "114212",
        "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113",
        "114311", "411113", "411311", "113141", "114131", "311141",
        "411131", "211412", "211214", "211232", "2331112"
    ];

    public BarcodeSymbol? Encode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(c => c is < ' ' or > '~'))
            return null;

        var codewords = new List<int>(value.Length + 3) { StartB };
        codewords.AddRange(value.Select(c => c - ' '));

        var checksum = StartB;
        for (var i = 1; i < codewords.Count; i++)
            checksum += codewords[i] * i;
        codewords.Add(checksum % 103);
        codewords.Add(Stop);

        var bars = new List<BarcodeBar>();
        var x = QuietZone;
        foreach (var codeword in codewords)
        {
            var pattern = Patterns[codeword];
            for (var i = 0; i < pattern.Length; i++)
            {
                var width = pattern[i] - '0';
                if (i % 2 == 0)
                    bars.Add(new BarcodeBar(x, width));
                x += width;
            }
        }

        return new BarcodeSymbol(value, codewords, bars, x + QuietZone);
    }
}
