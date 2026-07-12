using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sklad.Localization;
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

    [Theory]
    [InlineData("required", "data-val-required")]
    [InlineData("range", "data-val-range")]
    [InlineData("length", "data-val-length")]
    [InlineData("lengthrange", "data-val-length")]
    [InlineData("minlength", "data-val-minlength")]
    [InlineData("email", "data-val-email")]
    public void Default_validation_adapters_emit_Bulgarian_client_messages(
        string kind,
        string messageAttribute)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        using var servicesProvider = services.BuildServiceProvider();
        var factory = servicesProvider.GetRequiredService<IStringLocalizerFactory>();
        var localizer = factory.Create(typeof(SharedResource));

        ValidationAttribute attribute = kind switch
        {
            "required" => new RequiredAttribute(),
            "range" => new RangeAttribute(1, 5),
            "length" => new StringLengthAttribute(5),
            "lengthrange" => new StringLengthAttribute(5) { MinimumLength = 2 },
            "minlength" => new MinLengthAttribute(3),
            "email" => new EmailAddressAttribute(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("bg-BG");
            CultureInfo.CurrentUICulture = new CultureInfo("bg-BG");

            var adapter = new LocalizedValidationAttributeAdapterProvider()
                .GetAttributeAdapter(attribute, localizer);
            Assert.NotNull(adapter);

            var metadataProvider = new EmptyModelMetadataProvider();
            var metadata = metadataProvider.GetMetadataForType(typeof(string));
            var attributes = new Dictionary<string, string>();
            var context = new ClientModelValidationContext(
                new ActionContext(), metadata, metadataProvider, attributes);

            adapter.AddValidation(context);

            var message = Assert.Contains(messageAttribute, attributes);
            Assert.DoesNotContain("The ", message, StringComparison.Ordinal);
            Assert.Matches("[А-Яа-я]", message);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Validation_adapter_preserves_an_explicit_message_key()
    {
        var attribute = new RangeAttribute(0, int.MaxValue)
        {
            ErrorMessage = "Quantity must be 0 or greater."
        };

        new LocalizedValidationAttributeAdapterProvider()
            .GetAttributeAdapter(attribute, stringLocalizer: null);

        Assert.Equal("Quantity must be 0 or greater.", attribute.ErrorMessage);
    }

    [Fact]
    public void Every_model_validation_attribute_has_a_Bulgarian_message()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        using var servicesProvider = services.BuildServiceProvider();
        var localizer = servicesProvider.GetRequiredService<IStringLocalizerFactory>()
            .Create(typeof(SharedResource));
        var adapterProvider = new LocalizedValidationAttributeAdapterProvider();
        var failures = new List<string>();

        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("bg-BG");
            var modelTypes = typeof(SharedResource).Assembly.GetTypes()
                .Where(t => t.Namespace is "Sklad.Models" or "Sklad.ViewModels");

            foreach (var property in modelTypes.SelectMany(t => t.GetProperties()))
            {
                foreach (var attribute in property.GetCustomAttributes(inherit: true)
                    .OfType<ValidationAttribute>()
                    .Where(a => a.GetType() != typeof(DataTypeAttribute)))
                {
                    adapterProvider.GetAttributeAdapter(attribute, localizer);

                    if (attribute.ErrorMessage is { } key)
                    {
                        if (localizer[key].ResourceNotFound)
                            failures.Add($"{property.DeclaringType!.Name}.{property.Name}: {key}");
                    }
                    else if (attribute.ErrorMessageResourceName is null ||
                             attribute.ErrorMessageResourceType is null)
                    {
                        failures.Add($"{property.DeclaringType!.Name}.{property.Name}: {attribute.GetType().Name}");
                    }
                }
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        Assert.True(failures.Count == 0,
            "Validation attributes without a Bulgarian message:" + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_view_model_validation_field_has_a_localized_display_name()
    {
        var translated = XDocument
            .Load(Path.Combine(TestPaths.App(), "Resources", "SharedResource.bg.resx"))
            .Root!
            .Elements("data")
            .Select(data => data.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();

        var properties = typeof(SharedResource).Assembly.GetTypes()
            .Where(type => type.Namespace == "Sklad.ViewModels")
            .SelectMany(type => type.GetProperties());

        foreach (var property in properties)
        {
            var validates = property.GetCustomAttributes<ValidationAttribute>()
                .Any(attribute => attribute.GetType() != typeof(DataTypeAttribute));
            if (!validates)
                continue;

            var displayKey = property.GetCustomAttribute<DisplayAttribute>()?.Name;
            if (string.IsNullOrWhiteSpace(displayKey) || !translated.Contains(displayKey))
                failures.Add($"{property.DeclaringType!.Name}.{property.Name}: {displayKey ?? "<missing>"}");
        }

        Assert.True(failures.Count == 0,
            "Validated view-model fields without a localized display name:" + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    // Anchored on the opening quote so dynamic keys (@L[Enums.Key(x)]) are skipped:
    // they cannot be resolved statically and are covered by the enum key entries.
    private static readonly Regex LocalizedKey = new(
        "(?:(?<![A-Za-z0-9_])L|_l)\\[\"((?:[^\"\\\\]|\\\\.)*)\"",
        RegexOptions.Compiled);

    [Fact]
    public void Resx_covers_every_localized_key()
    {
        var app = Path.Combine(TestPaths.RepoRoot(), "Sklad.NET");

        var translated = XDocument
            .Load(Path.Combine(app, "Resources", "SharedResource.bg.resx"))
            .Root!
            .Elements("data")
            .Select(d => d.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);

        var sources = Directory
            .EnumerateFiles(Path.Combine(app, "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(app, "Controllers"), "*.cs"))
            .Concat(Directory.EnumerateFiles(Path.Combine(app, "Services"), "*.cs"));

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
}
