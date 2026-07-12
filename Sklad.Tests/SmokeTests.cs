using Sklad.Models;

namespace Sklad.Tests;

public class SmokeTests
{
    [Fact]
    public void TestDb_creates_schema_and_saves_a_tire()
    {
        using var db = new TestDb();
        using (var context = db.CreateContext())
        {
            context.Tires.Add(new Tire
            {
                Sku = "TEST-001", Brand = "Test", Model = "T1",
                Width = 205, Profile = 55, Diameter = 16,
                Season = Season.Summer, Type = TireType.New,
                UnitPrice = 100m, Quantity = 5, MinStock = 2
            });
            context.SaveChanges();
        }
        using (var context = db.CreateContext())
        {
            Assert.Single(context.Tires);
        }
    }

    [Fact]
    public void Chart_js_is_vendored_and_tracked()
    {
        var lib = Path.Combine(TestPaths.App(), "wwwroot", "lib", "chart.js");
        var bundle = Path.Combine(lib, "dist", "chart.umd.js");

        Assert.True(File.Exists(bundle),
            "Chart.js must be self-hosted; the report page loads it from wwwroot.");
        Assert.True(File.Exists(Path.Combine(lib, "LICENSE.md")),
            "Every vendored library in wwwroot/lib ships its licence.");

        // A failed re-vendor writes an error page to the same path, which File.Exists
        // cannot tell from the real bundle.
        Assert.True(new FileInfo(bundle).Length > 100_000, "chart.umd.js looks truncated.");
        Assert.Contains("Chart", File.ReadAllText(bundle)[..2000], StringComparison.Ordinal);
    }

    [Fact]
    public void Floor_flow_keeps_its_accessible_page_structure()
    {
        var views = Path.Combine(TestPaths.App(), "Views");
        var tire = File.ReadAllText(Path.Combine(views, "Floor", "Tire.cshtml"));
        var layout = File.ReadAllText(Path.Combine(views, "Shared", "_FloorLayout.cshtml"));

        Assert.Contains("<h1 class=\"floor-sku\">", tire, StringComparison.Ordinal);
        Assert.Contains("class=\"skip-link\" href=\"#main-content\"", layout, StringComparison.Ordinal);
    }
}
