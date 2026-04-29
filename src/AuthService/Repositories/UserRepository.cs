using AuthService.Data;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace AuthService.Repositories;

public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByIdAsync(int id);
    Task<User> CreateAsync(User user);
    Task<IEnumerable<User>> GetAllAsync();
}

public class SqlUserRepository : IUserRepository
{
    private readonly AuthDbContext _context;
    private readonly IDistributedCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public SqlUserRepository(AuthDbContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        var cacheKey = $"user:username:{username.ToLowerInvariant()}";
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey);
            if (cached is not null)
                return JsonSerializer.Deserialize<User>(cached);
        }
        catch { /* Redis unavailable — fall through to DB */ }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user is not null)
        {
            try
            {
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(user),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
            }
            catch { /* Redis unavailable */ }
        }

        return user;
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        var cacheKey = $"user:id:{id}";
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey);
            if (cached is not null)
                return JsonSerializer.Deserialize<User>(cached);
        }
        catch { /* Redis unavailable */ }

        var user = await _context.Users.FindAsync(id);

        if (user is not null)
        {
            try
            {
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(user),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
            }
            catch { /* Redis unavailable */ }
        }

        return user;
    }

    public async Task<User> CreateAsync(User user)
    {
        user.CreatedAt = DateTime.UtcNow;
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        return await _context.Users.ToListAsync();
    }
}
