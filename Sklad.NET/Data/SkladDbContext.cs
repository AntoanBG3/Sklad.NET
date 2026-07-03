using Microsoft.EntityFrameworkCore;
using Sklad.Models;

namespace Sklad.Data;

public class SkladDbContext : DbContext
{
    public SkladDbContext(DbContextOptions<SkladDbContext> options) : base(options) { }

    public DbSet<Tire> Tires => Set<Tire>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tire>(entity =>
        {
            entity.HasIndex(t => t.Sku).IsUnique();

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
