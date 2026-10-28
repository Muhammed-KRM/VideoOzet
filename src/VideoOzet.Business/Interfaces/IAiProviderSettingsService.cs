using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public record AiProviderSettingsDto
{
    /// <summary>"openrouter", "groq" veya "antigravity"</summary>
    public string ActiveProvider { get; init; } = "openrouter";

    /// <summary>OpenRouter API Anahtarı (Kullanıcı tarafından arayüzden girilir)</summary>
    public string OpenRouterApiKey { get; set; } = "";

    /// <summary>OpenRouter Base URL</summary>
    public string OpenRouterBaseUrl { get; init; } = "https://openrouter.ai/api/v1";

    /// <summary>OpenRouter Modeli</summary>
    public string OpenRouterModel { get; set; } = "google/gemini-3.8-flash";

    /// <summary>Groq Cloud API Anahtarı (Whisper STT ve/veya Groq LLM için arayüzden girilir)</summary>
    public string GroqApiKey { get; set; } = "";

    /// <summary>Groq Cloud LLM Modeli</summary>
    public string GroqModel { get; set; } = "openai/gpt-oss-120b";

    /// <summary>Antigravity / Yerel Proxy URL</summary>
    public string AntigravityProxyUrl { get; init; } = "http://localhost:8045/v1";

    /// <summary>Antigravity / Yerel Proxy Key (Kullanıcı tarafından arayüzden girilir)</summary>
    public string AntigravityProxyKey { get; set; } = "";

    /// <summary>Antigravity / Yerel Proxy Model</summary>
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
