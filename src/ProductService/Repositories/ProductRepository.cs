using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ProductService.Data;
using ProductService.Models;
using System.Text.Json;

namespace ProductService.Repositories;

public interface IProductRepository
{
    Task<IEnumerable<Product>> GetAllAsync();
    Task<Product?> GetByIdAsync(int id);
    Task<Product> CreateAsync(Product product);
    Task<Product?> UpdateAsync(int id, UpdateProductRequest request);
    Task<bool> DeleteAsync(int id);
    Task<IEnumerable<Product>> GetByCategoryAsync(string category);
}

public class SqlProductRepository : IProductRepository
{
    private readonly ProductDbContext _context;
    private readonly IDistributedCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public SqlProductRepository(ProductDbContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        const string cacheKey = "products:all";
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey);
            if (cached is not null)
                return JsonSerializer.Deserialize<List<Product>>(cached) ?? [];
        }
        catch { /* Redis unavailable */ }

        var products = await _context.Products.Where(p => p.IsActive).ToListAsync();

        try
        {
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(products),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
        }
        catch { /* Redis unavailable */ }

        return products;
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        var cacheKey = $"product:{id}";
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey);
            if (cached is not null)
                return JsonSerializer.Deserialize<Product>(cached);
        }
        catch { /* Redis unavailable */ }

        var product = await _context.Products.FindAsync(id);

        if (product is not null)
        {
            try
            {
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(product),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
            }
            catch { /* Redis unavailable */ }
        }

        return product;
    }

    public async Task<Product> CreateAsync(Product product)
    {
        product.CreatedAt = DateTime.UtcNow;
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        await InvalidateCacheAsync(product.Id);
        return product;
    }

    public async Task<Product?> UpdateAsync(int id, UpdateProductRequest request)
    {
        var product = await _context.Products.FindAsync(id);
        if (product is null) return null;

        if (request.Name is not null) product.Name = request.Name;
        if (request.Description is not null) product.Description = request.Description;
        if (request.Price.HasValue) product.Price = request.Price.Value;
        if (request.StockQuantity.HasValue) product.StockQuantity = request.StockQuantity.Value;
        if (request.Category is not null) product.Category = request.Category;
        if (request.IsActive.HasValue) product.IsActive = request.IsActive.Value;
        product.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await InvalidateCacheAsync(id);
        return product;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product is null) return false;

        _context.Products.Remove(product);
        await _context.SaveChangesAsync();
        await InvalidateCacheAsync(id);
        return true;
    }

    public async Task<IEnumerable<Product>> GetByCategoryAsync(string category)
    {
        return await _context.Products
            .Where(p => p.Category == category && p.IsActive)
            .ToListAsync();
    }

    private async Task InvalidateCacheAsync(int id)
    {
        try
        {
            await _cache.RemoveAsync($"product:{id}");
            await _cache.RemoveAsync("products:all");
        }
        catch { /* Redis unavailable */ }
    }
}
