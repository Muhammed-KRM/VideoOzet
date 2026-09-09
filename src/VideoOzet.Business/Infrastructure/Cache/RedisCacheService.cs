using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.Cache;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiration)
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.StringSetAsync(key, value, expiration, When.NotExists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while setting key {Key} if not exists in Redis.", key);
            return false;
        }
    }

    public async Task<string?> GetAsync(string key)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting key {Key} from Redis.", key);
            return null;
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while removing key {Key} from Redis.", key);
        }
    }
}
