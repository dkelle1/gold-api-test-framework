using Microsoft.EntityFrameworkCore;
using OrderService.Models;

namespace OrderService.Data;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(o => o.CustomerName).HasMaxLength(200).IsRequired();
            entity.Property(o => o.CustomerEmail).HasMaxLength(200).IsRequired();
            entity.Property(o => o.UnitPrice).HasColumnType("decimal(18,2)");
            entity.Property(o => o.TotalPrice).HasColumnType("decimal(18,2)");
            entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(o => o.CustomerEmail);
            entity.HasIndex(o => o.ProductId);
            entity.HasIndex(o => o.Status);
        });
    }
}
