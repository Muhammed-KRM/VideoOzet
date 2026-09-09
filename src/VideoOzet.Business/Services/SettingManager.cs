using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Business.Services;

public class SettingManager : ISettingService
{
    private readonly IRepository<GlobalSetting> _settingRepo;
    private readonly IMemoryCache _cache;
    private const string CachePrefix = "Setting_";

    public SettingManager(IRepository<GlobalSetting> settingRepo, IMemoryCache cache)
    {
        _settingRepo = settingRepo;
        _cache = cache;
    }

    public async Task<string> GetSettingAsync(string key, string defaultValue = "")
    {
        string cacheKey = CachePrefix + key;
        if (_cache.TryGetValue(cacheKey, out string? cachedValue) && cachedValue != null)
        {
            return cachedValue;
        }

        var setting = (await _settingRepo.FindAsync(s => s.Key == key)).FirstOrDefault();
        var value = setting?.Value ?? defaultValue;

        _cache.Set(cacheKey, value, TimeSpan.FromMinutes(10));
        return value;
    }

    public async Task<int> GetIntSettingAsync(string key, int defaultValue = 0)
    {
        var stringValue = await GetSettingAsync(key, defaultValue.ToString());
        return int.TryParse(stringValue, out int result) ? result : defaultValue;
    }

    public async Task SetSettingAsync(string key, string value, string? description = null)
    {
        var setting = (await _settingRepo.FindAsync(s => s.Key == key)).FirstOrDefault();

        if (setting == null)
        {
            setting = new GlobalSetting
            {
                Key = key,
                Value = value,
                Description = description
            };
            await _settingRepo.AddAsync(setting);
        }
        else
        {
            setting.Value = value;
            if (description != null) setting.Description = description;
            _settingRepo.Update(setting);
        }

        await _settingRepo.SaveChangesAsync();
        _cache.Remove(CachePrefix + key);
    }

    public async Task InitializeDefaultsAsync()
    {
        var defaultSettings = new List<(string Key, string Value, string Description)>
        {
            ("ListingCreationCost", "5", "İlan oluşturma jeton maliyeti"),
            ("MessageUnlockCost", "1", "Mesaj kilidi açma jeton maliyeti"),
            ("DirectOfferCost", "2", "Direkt teklif gönderme jeton maliyeti")
        };

        foreach (var (key, value, desc) in defaultSettings)
        {
            var exists = (await _settingRepo.FindAsync(s => s.Key == key)).Any();
            if (!exists)
            {
                await SetSettingAsync(key, value, desc);
            }
        }
    }
}
