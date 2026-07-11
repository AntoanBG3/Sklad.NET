using Microsoft.Extensions.Logging.Abstractions;
using Sklad.Data;
using Sklad.Models;
using Sklad.Services;

namespace Sklad.Tests;

public class ShopSettingsServiceTests
{
    private static ShopSettingsService CreateService(SkladDbContext db) =>
        new(db, NullLogger<ShopSettingsService>.Instance, new DefaultCultureCache());

    [Fact]
    public async Task Get_on_empty_database_returns_blank_settings()
    {
        using var testDb = new TestDb();
        using var db = testDb.CreateContext();

        var settings = await CreateService(db).GetAsync();

        Assert.NotNull(settings);
        Assert.Null(settings.Name);
        Assert.Null(settings.Address);
    }

    [Fact]
    public async Task Save_then_get_round_trips_every_field()
    {
        using var testDb = new TestDb();
        using (var db = testDb.CreateContext())
        {
            await CreateService(db).SaveAsync(new ShopSettings
            {
                Name = "Гуми Сервиз ЕООД",
                Address = "ул. Витоша 1, София",
                VatNumber = "BG123456789",
                Phone = "+359 2 000 0000",
                Email = "shop@example.bg",
            });
        }

        using (var db = testDb.CreateContext())
        {
            var settings = await CreateService(db).GetAsync();

            Assert.Equal("Гуми Сервиз ЕООД", settings.Name);
            Assert.Equal("ул. Витоша 1, София", settings.Address);
            Assert.Equal("BG123456789", settings.VatNumber);
            Assert.Equal("+359 2 000 0000", settings.Phone);
            Assert.Equal("shop@example.bg", settings.Email);
        }
    }

    [Fact]
    public async Task Save_twice_updates_the_singleton_instead_of_inserting_a_second_row()
    {
        using var testDb = new TestDb();

        using (var db = testDb.CreateContext())
            await CreateService(db).SaveAsync(new ShopSettings { Name = "First" });

        using (var db = testDb.CreateContext())
            await CreateService(db).SaveAsync(new ShopSettings { Name = "Second" });

        using (var db = testDb.CreateContext())
        {
            Assert.Equal(1, db.ShopSettings.Count());
            Assert.Equal(ShopSettings.SingletonId, db.ShopSettings.Single().Id);
            Assert.Equal("Second", db.ShopSettings.Single().Name);
        }
    }

    [Fact]
    public async Task Get_on_empty_database_leaves_every_preference_unset()
    {
        using var testDb = new TestDb();
        using var db = testDb.CreateContext();

        var settings = await CreateService(db).GetAsync();

        Assert.Null(settings.DefaultMinStock);
        Assert.Null(settings.PageSize);
        Assert.Null(settings.DefaultCulture);
        Assert.Null(settings.ReportRangeMonths);
    }

    [Fact]
    public async Task Save_then_get_round_trips_every_preference()
    {
        using var testDb = new TestDb();
        using (var db = testDb.CreateContext())
        {
            await CreateService(db).SaveAsync(new ShopSettings
            {
                DefaultMinStock = 4,
                PageSize = 25,
                DefaultCulture = "en-GB",
                ReportRangeMonths = 6,
            });
        }

        using (var db = testDb.CreateContext())
        {
            var settings = await CreateService(db).GetAsync();

            Assert.Equal(4, settings.DefaultMinStock);
            Assert.Equal(25, settings.PageSize);
            Assert.Equal("en-GB", settings.DefaultCulture);
            Assert.Equal(6, settings.ReportRangeMonths);
        }
    }

    [Fact]
    public async Task Save_clears_preferences_that_were_blanked()
    {
        using var testDb = new TestDb();

        using (var db = testDb.CreateContext())
            await CreateService(db).SaveAsync(new ShopSettings { PageSize = 25, ReportRangeMonths = 6 });

        using (var db = testDb.CreateContext())
            await CreateService(db).SaveAsync(new ShopSettings());

        using (var db = testDb.CreateContext())
        {
            var settings = await CreateService(db).GetAsync();
            Assert.Null(settings.PageSize);
            Assert.Null(settings.ReportRangeMonths);
        }
    }

    [Fact]
    public async Task Save_clears_fields_that_were_blanked()
    {
        using var testDb = new TestDb();

        using (var db = testDb.CreateContext())
            await CreateService(db).SaveAsync(new ShopSettings { Name = "Shop", Phone = "123" });

        using (var db = testDb.CreateContext())
            await CreateService(db).SaveAsync(new ShopSettings { Name = "Shop", Phone = null });

        using (var db = testDb.CreateContext())
            Assert.Null((await CreateService(db).GetAsync()).Phone);
    }
}
