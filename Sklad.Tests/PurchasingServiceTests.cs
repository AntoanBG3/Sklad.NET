using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sklad.Data;
using Sklad.Models;
using Sklad.Services;

namespace Sklad.Tests;

public class PurchasingServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static PurchasingService CreateService(SkladDbContext context)
        => new(context, NullLogger<PurchasingService>.Instance);

    private static Tire NewTire(string sku, int qty = 5) => new()
    {
        Sku = sku, Brand = "Test", Model = "M", Width = 205, Profile = 55, Diameter = 16,
        Season = Season.Summer, Type = TireType.New, UnitPrice = 100m, Quantity = qty, MinStock = 2
    };

    private async Task<(int SupplierId, int TireId)> SeedAsync()
    {
        await using var context = _db.CreateContext();
        var supplier = new Supplier { Name = "Tyre Trade" };
        var tire = NewTire("PO-T1");
        context.Suppliers.Add(supplier);
        context.Tires.Add(tire);
        await context.SaveChangesAsync();
        return (supplier.Id, tire.Id);
    }

    [Fact]
    public async Task Duplicate_supplier_name_is_rejected_case_insensitively()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        await service.CreateSupplierAsync(new Supplier { Name = "Tyre Trade" });

        await Assert.ThrowsAsync<DuplicateSupplierNameException>(
            () => service.CreateSupplierAsync(new Supplier { Name = "TYRE TRADE" }));
    }

    [Fact]
    public async Task Supplier_with_orders_cannot_be_deleted()
    {
        var (supplierId, tireId) = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        await service.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 2, 90m)]);

        await Assert.ThrowsAsync<SupplierHasOrdersException>(() => service.DeleteSupplierAsync(supplierId));
    }

    [Fact]
    public async Task Order_needs_at_least_one_line()
    {
        var (supplierId, _) = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        await Assert.ThrowsAsync<EmptyPurchaseOrderException>(
            () => service.CreateOrderAsync(supplierId, null, []));
    }

    [Fact]
    public async Task Order_line_with_zero_quantity_is_rejected()
    {
        var (supplierId, tireId) = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        await Assert.ThrowsAsync<InvalidOrderLineException>(
            () => service.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 0, 90m)]));
    }

    [Fact]
    public async Task Order_with_unknown_tire_is_rejected()
    {
        var (supplierId, _) = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        await Assert.ThrowsAsync<TireNotFoundException>(
            () => service.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(99999, 1, 90m)]));
    }

    [Fact]
    public async Task Creating_an_order_does_not_touch_stock()
    {
        var (supplierId, tireId) = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        await service.CreateOrderAsync(supplierId, "restock", [new PurchaseOrderLine(tireId, 4, 90m)], "boss");

        var tire = await context.Tires.FindAsync(tireId);
        Assert.Equal(5, tire!.Quantity);
        Assert.Equal(0, await context.StockMovements.CountAsync());
    }

    [Fact]
    public async Task Receiving_an_order_adds_stock_and_writes_ledger_movements()
    {
        var (supplierId, tireId) = await SeedAsync();
        int orderId;
        await using (var setup = _db.CreateContext())
        {
            var service = CreateService(setup);
            var created = await service.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 4, 90m)], "boss");
            await service.MarkOrderedAsync(created.Id);
            orderId = created.Id;
        }

        await using var context = _db.CreateContext();
        await CreateService(context).ReceiveAsync(orderId, "maria");

        await using var check = _db.CreateContext();
        var tire = await check.Tires.FindAsync(tireId);
        Assert.Equal(9, tire!.Quantity);

        var movement = Assert.Single(await check.StockMovements.ToListAsync());
        Assert.Equal(MovementType.In, movement.MovementType);
        Assert.Equal(4, movement.Quantity);
        Assert.Equal("maria", movement.UserName);
        Assert.Contains($"PO-{orderId:D4}", movement.Note);

        var order = await check.PurchaseOrders.FindAsync(orderId);
        Assert.Equal(PurchaseOrderStatus.Received, order!.Status);
        Assert.NotNull(order.ReceivedAt);
        Assert.Equal("maria", order.ReceivedBy);
    }

    [Fact]
    public async Task Receiving_twice_is_rejected()
    {
        var (supplierId, tireId) = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var order = await service.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 4, 90m)]);
        await service.ReceiveAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => service.ReceiveAsync(order.Id));

        await using var check = _db.CreateContext();
        Assert.Equal(9, (await check.Tires.FindAsync(tireId))!.Quantity);
    }

    [Fact]
    public async Task Receiving_survives_a_concurrent_tire_update()
    {
        var (supplierId, tireId) = await SeedAsync();
        int orderId;
        await using (var setup = _db.CreateContext())
        {
            var order = await CreateService(setup).CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 4, 90m)]);
            orderId = order.Id;
        }

        // BumpVersionsOnSave forces a version conflict on every SaveChanges, so
        // receive exhausts its retries and must surface the typed exception.
        await using var context = _db.CreateContext(new BumpVersionsOnSave(_db.Connection));
        await Assert.ThrowsAsync<StaleTireException>(() => CreateService(context).ReceiveAsync(orderId));
    }

    [Fact]
    public async Task Draft_can_be_edited_ordered_cannot()
    {
        var (supplierId, tireId) = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var order = await service.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 4, 90m)]);

        await service.UpdateDraftAsync(order.Id, supplierId, "updated", [new PurchaseOrderLine(tireId, 2, 80m)]);
        var updated = await service.GetOrderAsync(order.Id);
        Assert.Equal("updated", updated!.Note);
        Assert.Equal(2, Assert.Single(updated.Items).Quantity);

        await service.MarkOrderedAsync(order.Id);
        await Assert.ThrowsAsync<InvalidOrderStateException>(
            () => service.UpdateDraftAsync(order.Id, supplierId, null, [new PurchaseOrderLine(tireId, 1, 80m)]));
    }

    [Fact]
    public async Task Cancelled_order_cannot_be_received()
    {
        var (supplierId, tireId) = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var order = await service.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 4, 90m)]);
        await service.CancelAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => service.ReceiveAsync(order.Id));

        await using var check = _db.CreateContext();
        Assert.Equal(5, (await check.Tires.FindAsync(tireId))!.Quantity);
    }

    [Fact]
    public async Task Tire_on_an_order_cannot_be_deleted()
    {
        var (supplierId, tireId) = await SeedAsync();
        await using (var setup = _db.CreateContext())
        {
            await CreateService(setup).CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 1, 90m)]);
        }

        await using var context = _db.CreateContext();
        var inventory = new InventoryService(context, NullLogger<InventoryService>.Instance, new FakeLocalizer<SharedResource>());
        await Assert.ThrowsAsync<TireOnOrderException>(() => inventory.DeleteTireAsync(tireId));
    }

    [Fact]
    public async Task Orders_filter_by_status_and_supplier()
    {
        var (supplierId, tireId) = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var draft = await service.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 1, 90m)]);
        var received = await service.CreateOrderAsync(supplierId, null, [new PurchaseOrderLine(tireId, 2, 90m)]);
        await service.ReceiveAsync(received.Id);

        var drafts = await service.GetOrdersAsync(PurchaseOrderStatus.Draft, supplierId);
        Assert.Equal(draft.Id, Assert.Single(drafts.Items).Id);

        var all = await service.GetOrdersAsync(null, supplierId);
        Assert.Equal(2, all.TotalCount);
    }
}
