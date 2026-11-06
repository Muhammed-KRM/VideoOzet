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

    /// <summary>Genel / Varsayılan OpenRouter Modeli (Geriye uyumluluk için)</summary>
    public string OpenRouterModel { get; set; } = "google/gemini-3.8-flash";

    /// <summary>Metin & Video Özetleme Modeli (Örn: google/gemini-3.8-flash)</summary>
    public string? OpenRouterSummaryModel { get; set; }

    /// <summary>Video Planı & Senaryo Sentez Modeli (Örn: anthropic/claude-3.7-sonnet, argon vb.)</summary>
    public string? OpenRouterVideoPlanModel { get; set; }

    /// <summary>Kalite Kontrol (QC) Modeli</summary>
    public string? OpenRouterQcModel { get; set; }

    /// <summary>Groq Cloud API Anahtarı (Whisper STT ve/veya Groq LLM için arayüzden girilir)</summary>
    public string GroqApiKey { get; set; } = "";

    /// <summary>Groq Cloud LLM Modeli</summary>
    public string GroqModel { get; set; } = "openai/gpt-oss-120b";

    /// <summary>Groq Özetleme Modeli</summary>
    public string? GroqSummaryModel { get; set; }

    /// <summary>Groq Video Planı Modeli</summary>
    public string? GroqVideoPlanModel { get; set; }

    /// <summary>Groq QC Modeli</summary>
    public string? GroqQcModel { get; set; }

    /// <summary>Antigravity / Yerel Proxy URL</summary>
    public string AntigravityProxyUrl { get; init; } = "http://localhost:8045/v1";

    /// <summary>Antigravity / Yerel Proxy Key (Kullanıcı tarafından arayüzden girilir)</summary>
    public string AntigravityProxyKey { get; set; } = "";

    /// <summary>Antigravity / Yerel Proxy Model</summary>
    public string AntigravityProxyModel { get; init; } = "gemini-3.8-flash-tiered";

    /// <summary>Antigravity Proxy Özetleme Modeli</summary>
    public string? AntigravityProxySummaryModel { get; set; }

    /// <summary>Antigravity Proxy Video Planı Modeli</summary>
    public string? AntigravityProxyVideoPlanModel { get; set; }

    /// <summary>Antigravity Proxy QC Modeli</summary>
    public string? AntigravityProxyQcModel { get; set; }

    public string GetEffectiveSummaryModel()
    {
        if (ActiveProvider.Equals("openrouter", System.StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(OpenRouterSummaryModel) ? OpenRouterSummaryModel : (!string.IsNullOrWhiteSpace(OpenRouterModel) ? OpenRouterModel : "google/gemini-3.8-flash");
        if (ActiveProvider.Equals("groq", System.StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(GroqSummaryModel) ? GroqSummaryModel : (!string.IsNullOrWhiteSpace(GroqModel) ? GroqModel : "openai/gpt-oss-120b");
        return !string.IsNullOrWhiteSpace(AntigravityProxySummaryModel) ? AntigravityProxySummaryModel : (!string.IsNullOrWhiteSpace(AntigravityProxyModel) ? AntigravityProxyModel : "gemini-3.8-flash-tiered");
    }

    public string GetEffectiveVideoPlanModel()
    {
        if (ActiveProvider.Equals("openrouter", System.StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(OpenRouterVideoPlanModel) ? OpenRouterVideoPlanModel : (!string.IsNullOrWhiteSpace(OpenRouterModel) ? OpenRouterModel : "google/gemini-3.8-flash");
        if (ActiveProvider.Equals("groq", System.StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(GroqVideoPlanModel) ? GroqVideoPlanModel : (!string.IsNullOrWhiteSpace(GroqModel) ? GroqModel : "openai/gpt-oss-120b");
        return !string.IsNullOrWhiteSpace(AntigravityProxyVideoPlanModel) ? AntigravityProxyVideoPlanModel : (!string.IsNullOrWhiteSpace(AntigravityProxyModel) ? AntigravityProxyModel : "gemini-3.8-flash-tiered");
    }

    public string GetEffectiveQcModel()
    {
        if (ActiveProvider.Equals("openrouter", System.StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(OpenRouterQcModel) ? OpenRouterQcModel : GetEffectiveVideoPlanModel();
        if (ActiveProvider.Equals("groq", System.StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(GroqQcModel) ? GroqQcModel : GetEffectiveVideoPlanModel();
        return !string.IsNullOrWhiteSpace(AntigravityProxyQcModel) ? AntigravityProxyQcModel : GetEffectiveVideoPlanModel();
    }
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
