using Microsoft.EntityFrameworkCore;
using Sklad.Models;

namespace Sklad.Data;

public class SkladDbContext : DbContext
{
    public SkladDbContext(DbContextOptions<SkladDbContext> options) : base(options) { }

    public DbSet<Tire> Tires => Set<Tire>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

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
            entity.Property(t => t.Sku).UseCollation("NOCASE");
            entity.Property(t => t.Barcode).UseCollation("NOCASE");

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
    }
}
