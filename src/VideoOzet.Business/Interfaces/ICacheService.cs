using System;
using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public interface ICacheService
{
    Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiration);
    Task<string?> GetAsync(string key);
    Task RemoveAsync(string key);
}
