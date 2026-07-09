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
        Assert.True(File.Exists(Path.Combine(lib, "dist", "chart.umd.js")),
            "Chart.js must be self-hosted; the report page loads it from wwwroot.");
        Assert.True(File.Exists(Path.Combine(lib, "LICENSE.md")),
            "Every vendored library in wwwroot/lib ships its licence.");
    }
}
