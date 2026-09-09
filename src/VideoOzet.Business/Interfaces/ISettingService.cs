namespace VideoOzet.Business.Interfaces;

public interface ISettingService
{
    Task<string> GetSettingAsync(string key, string defaultValue = "");
    Task<int> GetIntSettingAsync(string key, int defaultValue = 0);
    Task SetSettingAsync(string key, string value, string? description = null);
    Task InitializeDefaultsAsync();
}
