using System.Globalization;
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
}
