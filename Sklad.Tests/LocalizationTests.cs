using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit.Abstractions;

namespace Sklad.Tests;

public class LocalizationTests
{
    private readonly ITestOutputHelper _output;

    public LocalizationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Satellite_assembly_contains_bg_resources()
    {
        var assembly = typeof(SharedResource).Assembly;
        var satellite = assembly.GetSatelliteAssembly(new CultureInfo("bg"));
        var names = satellite.GetManifestResourceNames();
        _output.WriteLine("Satellite manifest names: " + string.Join(", ", names));
        Assert.NotEmpty(names);
    }

    [Fact]
    public void Bulgarian_localizer_resolves_a_translated_string()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IStringLocalizerFactory>();
        var localizer = factory.Create(typeof(SharedResource));

        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("bg-BG");
            var result = localizer["Sign in"];
            _output.WriteLine($"Value: {result.Value}, NotFound: {result.ResourceNotFound}, SearchedLocation: {result.SearchedLocation}");
            Assert.False(result.ResourceNotFound);
            Assert.Equal("Вход", result.Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    // Anchored on the opening quote so dynamic keys (@L[Enums.Key(x)]) are skipped:
    // they cannot be resolved statically and are covered by the enum key entries.
    private static readonly Regex LocalizedKey = new(
        "(?:(?<![A-Za-z0-9_])L|_l)\\[\"((?:[^\"\\\\]|\\\\.)*)\"",
        RegexOptions.Compiled);

    [Fact]
    public void Resx_covers_every_localized_key()
    {
        var app = Path.Combine(RepoRoot(), "Sklad.NET");

        var translated = XDocument
            .Load(Path.Combine(app, "Resources", "SharedResource.bg.resx"))
            .Root!
            .Elements("data")
            .Select(d => d.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);

        var sources = Directory
            .EnumerateFiles(Path.Combine(app, "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(app, "Controllers"), "*.cs"));

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var scanned = 0;
        foreach (var file in sources)
        {
            foreach (Match match in LocalizedKey.Matches(File.ReadAllText(file)))
            {
                scanned++;
                var key = Unescape(match.Groups[1].Value);
                if (!translated.Contains(key))
                    missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }

        // Guards against a regex that silently matches nothing and passes vacuously.
        Assert.True(scanned > 200, $"Only {scanned} localized keys found; the scan is not working.");

        foreach (var gap in missing)
            _output.WriteLine(gap);

        Assert.True(missing.Count == 0,
            $"{missing.Count} localized string(s) have no Bulgarian translation in SharedResource.bg.resx:{Environment.NewLine}"
            + string.Join(Environment.NewLine, missing));
    }

    private static string Unescape(string literal) =>
        literal.Replace("\\\"", "\"", StringComparison.Ordinal)
               .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
}
