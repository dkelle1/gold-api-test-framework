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
        return await _context.Orders.Include(o => o.Items).ToListAsync();
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

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

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
            .Include(o => o.Items)
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
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return null;

        if (request.Status.HasValue) order.Status = request.Status.Value;

        if (request.Customer is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.Customer.Name)) order.CustomerName = request.Customer.Name;
            if (!string.IsNullOrWhiteSpace(request.Customer.Email)) order.CustomerEmail = request.Customer.Email;
            if (request.Customer.Address is not null)
            {
                order.CustomerStreet = request.Customer.Address.Street;
                order.CustomerCity = request.Customer.Address.City;
                order.CustomerPostalCode = request.Customer.Address.PostalCode;
                order.CustomerCountry = request.Customer.Address.Country;
            }
        }

        if (request.Shipping is not null)
        {
            order.ShippingMethod = request.Shipping.Method;
            if (request.Shipping.Address is not null)
            {
                order.ShippingStreet = request.Shipping.Address.Street;
                order.ShippingCity = request.Shipping.Address.City;
                order.ShippingPostalCode = request.Shipping.Address.PostalCode;
                order.ShippingCountry = request.Shipping.Address.Country;
            }
            if (request.Shipping.Notes is not null)
                order.ShippingNotes = request.Shipping.Notes;
        }

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
