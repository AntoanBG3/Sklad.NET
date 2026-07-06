using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sklad.Data;
using Sklad.Models;

namespace Sklad.Services;

public class PurchasingService : IPurchasingService
{
    private const int ConcurrencyRetries = 3;

    private readonly SkladDbContext _db;
    private readonly ILogger<PurchasingService> _logger;

    public PurchasingService(SkladDbContext db, ILogger<PurchasingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SupplierSummary>> GetSuppliersAsync()
    {
        var rows = await _db.Suppliers
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                Supplier = s,
                Open = s.PurchaseOrders.Count(o => o.Status == PurchaseOrderStatus.Draft || o.Status == PurchaseOrderStatus.Ordered),
                Total = s.PurchaseOrders.Count()
            })
            .ToListAsync();
        return rows.Select(r => new SupplierSummary(r.Supplier, r.Open, r.Total)).ToList();
    }

    public Task<Supplier?> GetSupplierAsync(int id)
        => _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);

    public async Task CreateSupplierAsync(Supplier supplier)
    {
        if (await _db.Suppliers.AnyAsync(s => s.Name == supplier.Name))
            throw new DuplicateSupplierNameException(supplier.Name);

        _db.Suppliers.Add(supplier);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueNameViolation(ex))
        {
            throw new DuplicateSupplierNameException(supplier.Name);
        }
        _logger.LogInformation("Supplier {Name} created", supplier.Name);
    }

    public async Task UpdateSupplierAsync(Supplier supplier)
    {
        var existing = await _db.Suppliers.FindAsync(supplier.Id)
            ?? throw new SupplierNotFoundException();

        if (await _db.Suppliers.AnyAsync(s => s.Name == supplier.Name && s.Id != supplier.Id))
            throw new DuplicateSupplierNameException(supplier.Name);

        existing.Name = supplier.Name;
        existing.ContactName = supplier.ContactName;
        existing.Phone = supplier.Phone;
        existing.Email = supplier.Email;
        existing.Notes = supplier.Notes;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueNameViolation(ex))
        {
            throw new DuplicateSupplierNameException(supplier.Name);
        }
    }

    public async Task DeleteSupplierAsync(int id)
    {
        var supplier = await _db.Suppliers.FindAsync(id);
        if (supplier is null) return;

        if (await _db.PurchaseOrders.AnyAsync(o => o.SupplierId == id))
            throw new SupplierHasOrdersException();

        _db.Suppliers.Remove(supplier);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Supplier {Name} (id {Id}) deleted", supplier.Name, id);
    }

    public async Task<PagedResult<PurchaseOrder>> GetOrdersAsync(PurchaseOrderStatus? status, int? supplierId = null, int page = 1, int pageSize = InventoryService.DefaultPageSize)
    {
        var q = _db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.Items)
            .AsQueryable();
        if (status.HasValue)
            q = q.Where(o => o.Status == status);
        if (supplierId.HasValue)
            q = q.Where(o => o.SupplierId == supplierId);
        q = q.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id);

        var total = await q.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<PurchaseOrder>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public Task<PurchaseOrder?> GetOrderAsync(int id)
        => _db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.Items).ThenInclude(i => i.Tire)
            .FirstOrDefaultAsync(o => o.Id == id);

    public async Task<PurchaseOrder> CreateOrderAsync(int supplierId, string? note, IReadOnlyList<PurchaseOrderLine> lines, string? userName = null)
    {
        await ValidateLinesAsync(lines);
        if (!await _db.Suppliers.AnyAsync(s => s.Id == supplierId))
            throw new SupplierNotFoundException();

        var order = new PurchaseOrder
        {
            SupplierId = supplierId,
            Status = PurchaseOrderStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            Note = note,
            CreatedBy = userName
        };
        foreach (var line in lines)
            order.Items.Add(new PurchaseOrderItem { TireId = line.TireId, Quantity = line.Quantity, UnitCost = line.UnitCost });

        _db.PurchaseOrders.Add(order);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Purchase order {Number} created for supplier {SupplierId} by {User}",
            order.Number, supplierId, userName ?? "unknown");
        return order;
    }

    public async Task UpdateDraftAsync(int id, int supplierId, string? note, IReadOnlyList<PurchaseOrderLine> lines)
    {
        await ValidateLinesAsync(lines);

        var order = await _db.PurchaseOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new PurchaseOrderNotFoundException();
        if (order.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOrderStateException(order.Status);
        if (!await _db.Suppliers.AnyAsync(s => s.Id == supplierId))
            throw new SupplierNotFoundException();

        order.SupplierId = supplierId;
        order.Note = note;
        _db.PurchaseOrderItems.RemoveRange(order.Items);
        order.Items.Clear();
        foreach (var line in lines)
            order.Items.Add(new PurchaseOrderItem { TireId = line.TireId, Quantity = line.Quantity, UnitCost = line.UnitCost });

        await _db.SaveChangesAsync();
    }

    public async Task MarkOrderedAsync(int id)
    {
        var order = await _db.PurchaseOrders.FindAsync(id)
            ?? throw new PurchaseOrderNotFoundException();
        if (order.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOrderStateException(order.Status);

        order.Status = PurchaseOrderStatus.Ordered;
        order.OrderedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Purchase order {Number} marked as ordered", order.Number);
    }

    // Receiving from Draft is allowed on purpose: a small shop often skips the
    // "ordered" step when goods arrive with the invoice.
    public async Task ReceiveAsync(int id, string? userName = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            var order = await _db.PurchaseOrders
                .Include(o => o.Supplier)
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id)
                ?? throw new PurchaseOrderNotFoundException();
            if (!order.IsOpen)
                throw new InvalidOrderStateException(order.Status);

            var tireIds = order.Items.Select(i => i.TireId).ToList();
            var tires = await _db.Tires.Where(t => tireIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
            var now = DateTime.UtcNow;
            foreach (var item in order.Items)
            {
                var tire = tires[item.TireId];
                tire.Quantity += item.Quantity;
                tire.Version++;
                _db.StockMovements.Add(new StockMovement
                {
                    TireId = item.TireId,
                    MovementType = MovementType.In,
                    Quantity = item.Quantity,
                    Date = now,
                    UserName = userName,
                    Note = $"{order.Number} — {order.Supplier.Name}"
                });
            }
            order.Status = PurchaseOrderStatus.Received;
            order.ReceivedAt = now;
            order.ReceivedBy = userName;

            try
            {
                await _db.SaveChangesAsync();
                _logger.LogInformation("Purchase order {Number} received ({Units} units) by {User}",
                    order.Number, order.TotalUnits, userName ?? "unknown");
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (attempt >= ConcurrencyRetries)
                {
                    _logger.LogWarning("Receiving order {OrderId} abandoned after {Attempts} version conflicts", id, attempt);
                    throw new StaleTireException();
                }
                _db.ChangeTracker.Clear();
            }
        }
    }

    public async Task CancelAsync(int id)
    {
        var order = await _db.PurchaseOrders.FindAsync(id)
            ?? throw new PurchaseOrderNotFoundException();
        if (!order.IsOpen)
            throw new InvalidOrderStateException(order.Status);

        order.Status = PurchaseOrderStatus.Cancelled;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Purchase order {Number} cancelled", order.Number);
    }

    private async Task ValidateLinesAsync(IReadOnlyList<PurchaseOrderLine> lines)
    {
        if (lines.Count == 0)
            throw new EmptyPurchaseOrderException();
        if (lines.Any(l => l.Quantity < 1 || l.UnitCost < 0))
            throw new InvalidOrderLineException();

        var tireIds = lines.Select(l => l.TireId).Distinct().ToList();
        var found = await _db.Tires.CountAsync(t => tireIds.Contains(t.Id));
        if (found != tireIds.Count)
            throw new TireNotFoundException();
    }

    private static bool IsUniqueNameViolation(DbUpdateException ex)
        => ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } inner
           && inner.Message.Contains("Name");
}
