using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Sklad.Data;
using Sklad.Models;
using Sklad.Services;

namespace Sklad.Tests;

public sealed class StocktakeServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_snapshots_only_the_selected_location()
    {
        await SeedAsync(
            NewTire("A-1", 8, "Rack A", version: 3),
            NewTire("A-2", 4, "Rack A", version: 1),
            NewTire("B-1", 9, "Rack B"));
        await using var context = _db.CreateContext();

        var result = await Service(context).CreateAsync("  Rack A  ", "  cycle count  ", "operator");

        Assert.Equal("Rack A", result.Location);
        Assert.Equal("cycle count", result.Note);
        Assert.Equal("operator", result.CreatedBy);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(new[] { 4, 8 }, result.Items.Select(item => item.ExpectedQuantity).Order().ToArray());
        Assert.Equal(new[] { 1, 3 }, result.Items.Select(item => item.ExpectedTireVersion).Order().ToArray());
        Assert.All(result.Items, item => Assert.Null(item.CountedQuantity));
    }

    [Fact]
    public async Task Create_rejects_empty_scope()
    {
        await SeedAsync(NewTire("A-1", 2, "Rack A"));
        await using var context = _db.CreateContext();

        await Assert.ThrowsAsync<EmptyStocktakeException>(
            () => Service(context).CreateAsync("Rack Z", null));
    }

    [Fact]
    public async Task Create_prevents_overlapping_open_counts()
    {
        await SeedAsync(NewTire("A-1", 2, "Rack A"), NewTire("B-1", 3, "Rack B"));
        await using (var first = _db.CreateContext())
            await Service(first).CreateAsync("Rack A", null);

        await using var second = _db.CreateContext();
        var error = await Assert.ThrowsAsync<ActiveStocktakeExistsException>(
            () => Service(second).CreateAsync(null, null));

        Assert.Equal("ST-0001", error.Number);
        Assert.Single(second.Stocktakes);
    }

    [Fact]
    public async Task Save_counts_is_resumable_and_does_not_touch_inventory()
    {
        await SeedAsync(NewTire("A-1", 8, "Rack A"), NewTire("A-2", 4, "Rack A"));
        int stocktakeId;
        IReadOnlyList<StocktakeItem> items;
        await using (var create = _db.CreateContext())
        {
            var stocktake = await Service(create).CreateAsync("Rack A", null);
            stocktakeId = stocktake.Id;
            items = stocktake.Items.OrderBy(item => item.Id).ToList();
        }

        await using (var save = _db.CreateContext())
        {
            await Service(save).SaveCountsAsync(stocktakeId, 0,
            [
                new StocktakeCount(items[0].Id, 7, "  damaged  "),
                new StocktakeCount(items[1].Id, null, null)
            ]);
        }

        await using var check = _db.CreateContext();
        var stored = await Service(check).GetStocktakeAsync(stocktakeId);
        Assert.Equal(1, stored!.Version);
        Assert.Equal(1, stored.CountedItems);
        Assert.Equal("damaged", stored.Items.Single(item => item.Id == items[0].Id).Note);
        Assert.Equal(new[] { 4, 8 }, check.Tires.Select(tire => tire.Quantity).Order().ToArray());
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public async Task Save_rejects_foreign_lines_without_partial_changes()
    {
        await SeedAsync(NewTire("A-1", 8, "Rack A"));
        int id, itemId;
        await using (var create = _db.CreateContext())
        {
            var stocktake = await Service(create).CreateAsync(null, null);
            id = stocktake.Id;
            itemId = Assert.Single(stocktake.Items).Id;
        }

        await using (var save = _db.CreateContext())
        {
            await Assert.ThrowsAsync<InvalidStocktakeLinesException>(() => Service(save).SaveCountsAsync(id, 0,
            [
                new StocktakeCount(itemId, 7, null),
                new StocktakeCount(99999, 1, null)
            ]));
        }

        await using var check = _db.CreateContext();
        var item = Assert.Single((await Service(check).GetStocktakeAsync(id))!.Items);
        Assert.Null(item.CountedQuantity);
        Assert.Equal(0, item.Stocktake.Version);
    }

    [Fact]
    public async Task Complete_requires_every_line_and_changes_nothing_on_failure()
    {
        await SeedAsync(NewTire("A-1", 8, "Rack A"), NewTire("A-2", 4, "Rack A"));
        int id;
        await using (var create = _db.CreateContext())
        {
            var stocktake = await Service(create).CreateAsync(null, null);
            id = stocktake.Id;
            await Service(create).SaveCountsAsync(id, 0,
                [new StocktakeCount(stocktake.Items.First().Id, 7, null)]);
        }

        await using (var complete = _db.CreateContext())
        {
            var error = await Assert.ThrowsAsync<IncompleteStocktakeException>(
                () => Service(complete).CompleteAsync(id, 1, "admin"));
            Assert.Equal(1, error.Remaining);
        }

        await using var check = _db.CreateContext();
        Assert.Equal(StocktakeStatus.Draft, (await check.Stocktakes.FindAsync(id))!.Status);
        Assert.Equal(new[] { 4, 8 }, check.Tires.Select(tire => tire.Quantity).Order().ToArray());
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public async Task Complete_posts_only_variances_as_atomic_adjustment_movements()
    {
        await SeedAsync(NewTire("A-1", 8, "Rack A"), NewTire("A-2", 4, "Rack A"));
        int id;
        await using (var create = _db.CreateContext())
        {
            var stocktake = await Service(create).CreateAsync(null, "audit");
            id = stocktake.Id;
            var items = stocktake.Items.OrderBy(item => item.ExpectedQuantity).ToList();
            await Service(create).SaveCountsAsync(id, 0,
            [
                new StocktakeCount(items[0].Id, 4, null),
                new StocktakeCount(items[1].Id, 6, "two missing")
            ]);
        }

        int variances;
        await using (var complete = _db.CreateContext())
            variances = await Service(complete).CompleteAsync(id, 1, "admin");

        Assert.Equal(1, variances);
        await using var check = _db.CreateContext();
        var stored = await Service(check).GetStocktakeAsync(id);
        Assert.Equal(StocktakeStatus.Completed, stored!.Status);
        Assert.Equal("admin", stored.CompletedBy);
        Assert.NotNull(stored.CompletedAt);
        Assert.Equal(2, stored.Version);
        Assert.Equal(1, stored.VarianceItems);

        var changed = await check.Tires.SingleAsync(tire => tire.Sku == "A-1");
        Assert.Equal(6, changed.Quantity);
        Assert.Equal(1, changed.Version);
        var unchanged = await check.Tires.SingleAsync(tire => tire.Sku == "A-2");
        Assert.Equal(4, unchanged.Quantity);
        Assert.Equal(0, unchanged.Version);

        var movement = Assert.Single(check.StockMovements);
        Assert.Equal(MovementType.Adjustment, movement.MovementType);
        Assert.Equal(6, movement.Quantity);
        Assert.Equal("admin", movement.UserName);
        Assert.Equal("ST-0001 — two missing", movement.Note);
    }

    [Fact]
    public async Task Complete_rejects_stock_that_moved_after_the_snapshot()
    {
        var tire = NewTire("A-1", 8, "Rack A");
        await SeedAsync(tire);
        int id, itemId;
        await using (var create = _db.CreateContext())
        {
            var stocktake = await Service(create).CreateAsync(null, null);
            id = stocktake.Id;
            itemId = Assert.Single(stocktake.Items).Id;
        }
        await using (var movement = _db.CreateContext())
            await Inventory(movement).RegisterMovementAsync(tire.Id, MovementType.In, 2, null);
        await using (var save = _db.CreateContext())
            await Service(save).SaveCountsAsync(id, 0, [new StocktakeCount(itemId, 8, null)]);

        await using (var complete = _db.CreateContext())
        {
            var error = await Assert.ThrowsAsync<StocktakeInventoryChangedException>(
                () => Service(complete).CompleteAsync(id, 1, "admin"));
            Assert.Equal("A-1", Assert.Single(error.Skus));
        }

        await using var check = _db.CreateContext();
        Assert.Equal(10, (await check.Tires.FindAsync(tire.Id))!.Quantity);
        Assert.Equal(StocktakeStatus.Draft, (await check.Stocktakes.FindAsync(id))!.Status);
        Assert.Single(check.StockMovements);
    }

    [Fact]
    public async Task Refresh_rebases_only_changed_tires_and_clears_their_counts()
    {
        var first = NewTire("A-1", 8, "Rack A");
        var second = NewTire("A-2", 4, "Rack A");
        await SeedAsync(first, second);
        int id;
        await using (var create = _db.CreateContext())
        {
            var stocktake = await Service(create).CreateAsync(null, null);
            id = stocktake.Id;
            await Service(create).SaveCountsAsync(id, 0, stocktake.Items
                .Select(item => new StocktakeCount(item.Id, item.ExpectedQuantity, null)).ToList());
        }
        await using (var movement = _db.CreateContext())
            await Inventory(movement).RegisterMovementAsync(first.Id, MovementType.Out, 1, null);

        int changed;
        await using (var refresh = _db.CreateContext())
            changed = await Service(refresh).RefreshChangedAsync(id, 1);

        Assert.Equal(1, changed);
        await using var check = _db.CreateContext();
        var stored = await Service(check).GetStocktakeAsync(id);
        Assert.Equal(2, stored!.Version);
        var rebased = stored.Items.Single(item => item.TireId == first.Id);
        Assert.Equal(7, rebased.ExpectedQuantity);
        Assert.Equal(1, rebased.ExpectedTireVersion);
        Assert.Null(rebased.CountedQuantity);
        Assert.Equal(4, stored.Items.Single(item => item.TireId == second.Id).CountedQuantity);
    }

    [Fact]
    public async Task Cancel_closes_the_count_without_changing_stock()
    {
        await SeedAsync(NewTire("A-1", 8, "Rack A"));
        int id;
        await using (var create = _db.CreateContext())
            id = (await Service(create).CreateAsync(null, null)).Id;

        await using (var cancel = _db.CreateContext())
            await Service(cancel).CancelAsync(id, 0);

        await using var check = _db.CreateContext();
        var stored = await check.Stocktakes.FindAsync(id);
        Assert.Equal(StocktakeStatus.Cancelled, stored!.Status);
        Assert.Equal(1, stored.Version);
        Assert.Equal(8, Assert.Single(check.Tires).Quantity);
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public async Task Stale_aggregate_version_is_rejected()
    {
        await SeedAsync(NewTire("A-1", 8, "Rack A"));
        int id;
        await using (var create = _db.CreateContext())
            id = (await Service(create).CreateAsync(null, null)).Id;

        await using var stale = _db.CreateContext();
        await Assert.ThrowsAsync<StaleStocktakeException>(() => Service(stale).CancelAsync(id, 99));
        Assert.Equal(StocktakeStatus.Draft, (await stale.Stocktakes.FindAsync(id))!.Status);
    }

    [Fact]
    public async Task Tire_referenced_by_stocktake_history_cannot_be_deleted()
    {
        var tire = NewTire("A-1", 8, "Rack A");
        await SeedAsync(tire);
        await using (var create = _db.CreateContext())
            await Service(create).CreateAsync(null, null);

        await using var delete = _db.CreateContext();
        await Assert.ThrowsAsync<TireOnStocktakeException>(() => Inventory(delete).DeleteTireAsync(tire.Id));
        Assert.NotNull(await delete.Tires.FindAsync(tire.Id));
    }

    private async Task SeedAsync(params Tire[] tires)
    {
        await using var context = _db.CreateContext();
        context.Tires.AddRange(tires);
        await context.SaveChangesAsync();
    }

    private static Tire NewTire(string sku, int quantity, string? location, int version = 0) => new()
    {
        Sku = sku, Brand = "Test", Model = sku, Width = 205, Profile = 55,
        Diameter = 16, Season = Season.Summer, Type = TireType.New,
        UnitPrice = 100m, Quantity = quantity, MinStock = 2, Location = location, Version = version
    };

    private static StocktakeService Service(SkladDbContext context)
        => new(context, NullLogger<StocktakeService>.Instance);

    private static InventoryService Inventory(SkladDbContext context)
        => new(context, NullLogger<InventoryService>.Instance);
}
