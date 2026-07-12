using Microsoft.EntityFrameworkCore;
using Sklad.Models;

namespace Sklad.Data;

public class SkladDbContext : DbContext
{
    public SkladDbContext(DbContextOptions<SkladDbContext> options) : base(options) { }

    public DbSet<Tire> Tires => Set<Tire>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<ShopSettings> ShopSettings => Set<ShopSettings>();

    // Maps to the connection-level unilower() (see SqliteFunctionsInterceptor);
    // the body is only a client-evaluation fallback.
    public static string UniLower(string value) => value.ToLowerInvariant();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDbFunction(typeof(SkladDbContext).GetMethod(nameof(UniLower), [typeof(string)])!)
            .HasName("unilower");

        modelBuilder.Entity<Tire>(entity =>
        {
            entity.HasIndex(t => t.Sku).IsUnique();

            // Operators and scanners shouldn't have to match SKU/barcode case,
            // and the unique index must not allow ABC-1 alongside abc-1.
            entity.Property(t => t.Sku).UseCollation(SqliteFunctionsInterceptor.UnicodeNoCaseCollation);
            entity.Property(t => t.Barcode).UseCollation(SqliteFunctionsInterceptor.UnicodeNoCaseCollation);

            entity.Property(t => t.UnitPrice).HasPrecision(18, 2);

            entity.Property(t => t.Version).IsConcurrencyToken();

            entity.HasMany(t => t.StockMovements)
                  .WithOne(m => m.Tire)
                  .HasForeignKey(m => m.TireId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.Property(m => m.Date).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Username).UseCollation(SqliteFunctionsInterceptor.UnicodeNoCaseCollation);
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasIndex(s => s.Name).IsUnique();
            entity.Property(s => s.Name).UseCollation(SqliteFunctionsInterceptor.UnicodeNoCaseCollation);

            entity.HasMany(s => s.PurchaseOrders)
                  .WithOne(o => o.Supplier)
                  .HasForeignKey(o => o.SupplierId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.Property(o => o.Version).IsConcurrencyToken();

            entity.HasMany(o => o.Items)
                  .WithOne(i => i.PurchaseOrder)
                  .HasForeignKey(i => i.PurchaseOrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PurchaseOrderItem>(entity =>
        {
            entity.Property(i => i.UnitCost).HasPrecision(18, 2);

            entity.HasOne(i => i.Tire)
                  .WithMany()
                  .HasForeignKey(i => i.TireId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
