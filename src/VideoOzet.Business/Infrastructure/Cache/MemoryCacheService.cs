using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.Cache;

public class MemoryCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, (string Value, DateTime Expiry)> _store = new();

    public Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiration)
    {
        var now = DateTime.UtcNow;
        if (_store.TryGetValue(key, out var existing) && existing.Expiry > now)
        {
            return Task.FromResult(false);
        }

        _store[key] = (value, now.Add(expiration));
        return Task.FromResult(true);
    }

    public Task SetAsync(string key, string value, TimeSpan expiration)
    {
        _store[key] = (value, DateTime.UtcNow.Add(expiration));
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.Expiry > DateTime.UtcNow)
            {
                return Task.FromResult<string?>(entry.Value);
            }
            _store.TryRemove(key, out _);
        }
        return Task.FromResult<string?>(null);
    }

    public Task RemoveAsync(string key)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
