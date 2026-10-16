using Microsoft.EntityFrameworkCore;
using OrderService.Models;

namespace OrderService.Data;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.CustomerName).HasMaxLength(200).IsRequired();
            entity.Property(o => o.CustomerEmail).HasMaxLength(200).IsRequired();
            entity.Property(o => o.CustomerStreet).HasMaxLength(200);
            entity.Property(o => o.CustomerCity).HasMaxLength(100);
            entity.Property(o => o.CustomerPostalCode).HasMaxLength(20);
            entity.Property(o => o.CustomerCountry).HasMaxLength(100);
            entity.Property(o => o.ShippingStreet).HasMaxLength(200);
            entity.Property(o => o.ShippingCity).HasMaxLength(100);
            entity.Property(o => o.ShippingPostalCode).HasMaxLength(20);
            entity.Property(o => o.ShippingCountry).HasMaxLength(100);
            entity.Property(o => o.ShippingNotes).HasMaxLength(500);
            entity.Property(o => o.TotalPrice).HasColumnType("decimal(18,2)");
            entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(o => o.ShippingMethod).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(o => o.CustomerEmail);
            entity.HasIndex(o => o.Status);

            entity.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
            entity.Property(i => i.LineTotal).HasColumnType("decimal(18,2)");
            entity.HasIndex(i => i.ProductId);
        });
    }
}
