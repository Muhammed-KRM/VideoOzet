using StackExchange.Redis;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.Cache;

public class RedisCacheService : ICacheService
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;

    public RedisCacheService(IConfiguration configuration)
    {
        var connectionString = configuration["Redis:ConnectionString"] ?? "localhost:6379";
        _redis = ConnectionMultiplexer.Connect(connectionString);
        _db = _redis.GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var value = await _db.StringGetAsync(key);
        if (value.IsNullOrEmpty) return default;
        
        return JsonSerializer.Deserialize<T>((string)value!);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        var serialized = JsonSerializer.Serialize(value);
        if (expiration.HasValue)
        {
            await _db.StringSetAsync(key, serialized, expiration.Value);
        }
        else
        {
            await _db.StringSetAsync(key, serialized);
        }
    }

    public async Task RemoveAsync(string key)
    {
        await _db.KeyDeleteAsync(key);
    }

    public async Task RemoveByPatternAsync(string pattern)
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: pattern);
        foreach (var key in keys)
        {
            await _db.KeyDeleteAsync(key);
        }
    }
}
