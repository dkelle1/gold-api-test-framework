using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using OrderService.Data;
using OrderService.Models;
using System.Text.Json;

namespace OrderService.Repositories;

public interface IOrderRepository
{
    Task<IEnumerable<Order>> GetAllAsync();
    Task<Order?> GetByIdAsync(int id);
    Task<IEnumerable<Order>> GetByCustomerEmailAsync(string email);
    Task<Order> CreateAsync(Order order);
    Task<Order?> UpdateAsync(int id, UpdateOrderRequest request);
    Task<bool> DeleteAsync(int id);
}

public class SqlOrderRepository : IOrderRepository
{
    private readonly OrderDbContext _context;
    private readonly IDistributedCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public SqlOrderRepository(OrderDbContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<IEnumerable<Order>> GetAllAsync()
    {
        return await _context.Orders.ToListAsync();
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        var cacheKey = $"order:{id}";
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey);
            if (cached is not null)
                return JsonSerializer.Deserialize<Order>(cached);
        }
        catch { /* Redis unavailable */ }

        var order = await _context.Orders.FindAsync(id);

        if (order is not null)
        {
            try
            {
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(order),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
            }
            catch { /* Redis unavailable */ }
        }

        return order;
    }

    public async Task<IEnumerable<Order>> GetByCustomerEmailAsync(string email)
    {
        return await _context.Orders
            .Where(o => o.CustomerEmail == email)
            .ToListAsync();
    }

    public async Task<Order> CreateAsync(Order order)
    {
        order.CreatedAt = DateTime.UtcNow;
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        return order;
    }

    public async Task<Order?> UpdateAsync(int id, UpdateOrderRequest request)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order is null) return null;

        if (request.Quantity.HasValue)
        {
            order.Quantity = request.Quantity.Value;
            order.TotalPrice = order.UnitPrice * request.Quantity.Value;
        }
        if (request.Status.HasValue) order.Status = request.Status.Value;
        if (request.CustomerName is not null) order.CustomerName = request.CustomerName;
        if (request.CustomerEmail is not null) order.CustomerEmail = request.CustomerEmail;
        order.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await InvalidateCacheAsync(id);
        return order;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order is null) return false;

        _context.Orders.Remove(order);
        await _context.SaveChangesAsync();
        await InvalidateCacheAsync(id);
        return true;
    }

    private async Task InvalidateCacheAsync(int id)
    {
        try { await _cache.RemoveAsync($"order:{id}"); }
        catch { /* Redis unavailable */ }
    }
}
