using InvoiceEdgeWorker.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvoiceEdgeWorker.Data;

public class InvoiceDbContext : DbContext
{
    public InvoiceDbContext(DbContextOptions<InvoiceDbContext> options) : base(options) { }

    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.VendorName).HasMaxLength(150);
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);

            // Map our custom Enum state cleanly as a text column in Postgres
            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50);

            entity.HasMany(e => e.LineItems)
                  .WithOne(e => e.Invoice)
                  .HasForeignKey(e => e.InvoiceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceLineItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Description).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Quantity).HasPrecision(18, 4);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.LineTotal).HasPrecision(18, 2);
        });
    }
}
