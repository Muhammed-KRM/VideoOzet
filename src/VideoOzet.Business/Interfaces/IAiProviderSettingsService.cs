using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public record AiProviderSettingsDto
{
    /// <summary>"openrouter" veya "antigravity"</summary>
    public string ActiveProvider { get; init; } = "antigravity";

    /// <summary>OpenRouter API Anahtarı</summary>
    public string OpenRouterApiKey { get; init; } = "";

    /// <summary>OpenRouter Base URL</summary>
    public string OpenRouterBaseUrl { get; init; } = "https://openrouter.ai/api/v1";

    /// <summary>OpenRouter Modeli</summary>
    public string OpenRouterModel { get; init; } = "google/gemini-2.5-flash";

    /// <summary>Antigravity Proxy URL</summary>
    public string AntigravityProxyUrl { get; init; } = "http://localhost:8045/v1";

    /// <summary>Antigravity Proxy Key</summary>
    public string AntigravityProxyKey { get; init; } = "sk-ea6e0c519b12403784eb5cdf79ca2615";

    /// <summary>Antigravity Proxy Model</summary>
    public string AntigravityProxyModel { get; init; } = "gemini-3.8-flash-tiered";
}

public record TestConnectionResultDto
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public long LatencyMs { get; init; }
}

public record ModelOptionDto
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
}

public interface IAiProviderSettingsService
{
    Task<AiProviderSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<AiProviderSettingsDto> UpdateSettingsAsync(AiProviderSettingsDto settings, CancellationToken ct = default);
    Task<TestConnectionResultDto> TestConnectionAsync(AiProviderSettingsDto settings, CancellationToken ct = default);
    Task<List<ModelOptionDto>> GetAvailableModelsAsync(string provider, CancellationToken ct = default);
}
