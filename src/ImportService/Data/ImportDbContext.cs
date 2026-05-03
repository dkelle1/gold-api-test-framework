using ImportService.Models;
using Microsoft.EntityFrameworkCore;

namespace ImportService.Data;

public class ImportDbContext : DbContext
{
    public ImportDbContext(DbContextOptions<ImportDbContext> options) : base(options)
    {
    }

    public DbSet<ProductImportBatch> ProductImportBatches { get; set; }
    public DbSet<ImportedProduct> ImportedProducts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductImportBatch>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(x => x.ErrorMessage).HasMaxLength(1000);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<ImportedProduct>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Price).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Category).HasMaxLength(100);

            entity.HasOne(x => x.Batch)
                .WithMany(x => x.ImportedProducts)
                .HasForeignKey(x => x.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.BatchId);
        });
    }
}
