using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Services;

public class AiProviderSettingsService : IAiProviderSettingsService
{
    private const string CacheKey = "videoozet:settings:ai_provider";
    private const string OpenRouterModelsCacheKey = "videoozet:settings:openrouter_models";
    private static readonly string SharedFilePath = Path.Combine(Path.GetTempPath(), "videoozet_ai_provider_settings.json");

    private readonly ICacheService _cache;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AiProviderSettingsService> _logger;

    private static AiProviderSettingsDto? _memoryFallback;

    public AiProviderSettingsService(
        ICacheService cache,
        IConfiguration config,
        IHttpClientFactory httpFactory,
        ILogger<AiProviderSettingsService> logger)
    {
        _cache = cache;
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<AiProviderSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _cache.GetAsync(CacheKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var cached = JsonSerializer.Deserialize<AiProviderSettingsDto>(json);
                if (cached is not null)
                {
                    _memoryFallback = cached;
                    return cached;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis üzerinden AI Provider ayarları okunamadı, yerel dosya/bellek yedeğine geçiliyor.");
        }

        // Yerel dosya yedeği (API ve Worker süreçleri arasında Redis kapalıyken de senkron kalması için)
        try
        {
            if (File.Exists(SharedFilePath))
            {
                var fileJson = await File.ReadAllTextAsync(SharedFilePath, ct);
                if (!string.IsNullOrWhiteSpace(fileJson))
                {
                    var fromFile = JsonSerializer.Deserialize<AiProviderSettingsDto>(fileJson);
                    if (fromFile is not null)
                    {
                        _memoryFallback = fromFile;
                        return fromFile;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Yerel AI Provider ayar dosyası okunamadı.");
        }

        if (_memoryFallback is not null)
            return _memoryFallback;

        var defaultSettings = new AiProviderSettingsDto
        {
            ActiveProvider = _config["AI_ACTIVE_PROVIDER"] ?? "antigravity",
            OpenRouterApiKey = _config["OpenRouter:ApiKey"] ?? _config["OPENROUTER_API_KEY"] ?? "",
            OpenRouterBaseUrl = _config["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1",
            OpenRouterModel = _config["OpenRouter:Model"] ?? "google/gemini-2.5-flash",
            AntigravityProxyUrl = _config["Antigravity:ProxyUrl"] ?? "http://localhost:8045/v1",
            AntigravityProxyKey = _config["GEMINI_API_KEY"] ?? "sk-ea6e0c519b12403784eb5cdf79ca2615",
            AntigravityProxyModel = _config["GEMINI_MODEL"] ?? "gemini-3.8-flash-tiered"
        };

        return defaultSettings;
    }

    public async Task<AiProviderSettingsDto> UpdateSettingsAsync(AiProviderSettingsDto settings, CancellationToken ct = default)
    {
        var current = await GetSettingsAsync(ct);

        var updated = new AiProviderSettingsDto
        {
            ActiveProvider = string.IsNullOrWhiteSpace(settings.ActiveProvider) ? current.ActiveProvider : settings.ActiveProvider.ToLowerInvariant(),
            OpenRouterApiKey = settings.OpenRouterApiKey?.Trim() ?? current.OpenRouterApiKey,
            OpenRouterBaseUrl = string.IsNullOrWhiteSpace(settings.OpenRouterBaseUrl) ? current.OpenRouterBaseUrl : settings.OpenRouterBaseUrl.Trim(),
            OpenRouterModel = string.IsNullOrWhiteSpace(settings.OpenRouterModel) ? current.OpenRouterModel : NormalizeModelId(settings.OpenRouterModel),
            AntigravityProxyUrl = string.IsNullOrWhiteSpace(settings.AntigravityProxyUrl) ? current.AntigravityProxyUrl : settings.AntigravityProxyUrl.Trim(),
            AntigravityProxyKey = string.IsNullOrWhiteSpace(settings.AntigravityProxyKey) ? current.AntigravityProxyKey : settings.AntigravityProxyKey.Trim(),
            AntigravityProxyModel = string.IsNullOrWhiteSpace(settings.AntigravityProxyModel) ? current.AntigravityProxyModel : settings.AntigravityProxyModel.Trim()
        };

        _memoryFallback = updated;
        var serialized = JsonSerializer.Serialize(updated);

        try
        {
            await _cache.SetAsync(CacheKey, serialized, TimeSpan.FromDays(365));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis üzerine AI Provider ayarları yazılamadı.");
        }

        try
        {
            await File.WriteAllTextAsync(SharedFilePath, serialized, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Yerel AI Provider ayar dosyasına yazılamadı.");
        }

        _logger.LogInformation("AI Provider ayarları güncellendi. Aktif Sağlayıcı: {Provider}, Model: {Model}",
            updated.ActiveProvider,
            updated.ActiveProvider == "openrouter" ? updated.OpenRouterModel : updated.AntigravityProxyModel);

        return updated;
    }

    public async Task<TestConnectionResultDto> TestConnectionAsync(AiProviderSettingsDto settings, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(12);

        try
        {
            if (settings.ActiveProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
                {
                    return new TestConnectionResultDto
                    {
                        Success = false,
                        Message = "OpenRouter API anahtarı boş olamaz.",
                        LatencyMs = sw.ElapsedMilliseconds
                    };
                }

                var url = $"{settings.OpenRouterBaseUrl.TrimEnd('/')}/auth/key";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenRouterApiKey);

                var res = await client.SendAsync(req, ct);
                sw.Stop();

                if (res.IsSuccessStatusCode)
                {
                    return new TestConnectionResultDto
                    {
                        Success = true,
                        Message = $"OpenRouter bağlantısı başarılı! Seçili model: {NormalizeModelId(settings.OpenRouterModel)}",
                        LatencyMs = sw.ElapsedMilliseconds
                    };
                }

                return new TestConnectionResultDto
                {
                    Success = false,
                    Message = $"OpenRouter bağlantı hatası: HTTP {(int)res.StatusCode}",
                    LatencyMs = sw.ElapsedMilliseconds
                };
            }
            else
            {
                var url = $"{settings.AntigravityProxyUrl.TrimEnd('/')}/models";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AntigravityProxyKey);

                var res = await client.SendAsync(req, ct);
                sw.Stop();

                if (res.IsSuccessStatusCode)
                {
                    return new TestConnectionResultDto
                    {
                        Success = true,
                        Message = $"Antigravity AI Proxy bağlantısı aktif! Seçili model: {settings.AntigravityProxyModel}",
                        LatencyMs = sw.ElapsedMilliseconds
                    };
                }

                return new TestConnectionResultDto
                {
                    Success = false,
                    Message = $"Antigravity AI Proxy yanıt vermedi: HTTP {(int)res.StatusCode}",
                    LatencyMs = sw.ElapsedMilliseconds
                };
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestConnectionResultDto
            {
                Success = false,
                Message = $"Bağlantı kurulamadı: {ex.Message}",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<List<ModelOptionDto>> GetAvailableModelsAsync(string provider, CancellationToken ct = default)
    {
        if (provider.Equals("antigravity", StringComparison.OrdinalIgnoreCase))
        {
            var settings = await GetSettingsAsync(ct);
            try
            {
                using var client = _httpFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{settings.AntigravityProxyUrl.TrimEnd('/')}/models");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AntigravityProxyKey);

                var res = await client.SendAsync(req, ct);
                if (res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                    {
                        var proxyModels = new List<ModelOptionDto>();
                        foreach (var item in dataArr.EnumerateArray())
                        {
                            var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(id))
                            {
                                proxyModels.Add(new ModelOptionDto
                                {
                                    Id = id,
                                    Name = $"{id} (Antigravity Proxy)",
                                    Category = "Antigravity Proxy",
                                    Description = "Yerel Antigravity AI Proxy modeli"
                                });
                            }
                        }

                        if (proxyModels.Count > 0)
                            return proxyModels;
                    }
                }
            }
            catch
            {
                // Proxy çalışmıyorsa varsayılan listeye dön
            }

            return new List<ModelOptionDto>
            {
                new() { Id = "gemini-3.8-flash-tiered", Name = "Gemini 3.8 Flash Tiered (Varsayılan)", Category = "Antigravity Proxy", Description = "VideoOzet varsayılan yüksek hızlı Gemini modeli" },
                new() { Id = "gemini-2.5-pro", Name = "Gemini 2.5 Pro (Antigravity)", Category = "Antigravity Proxy", Description = "Derin analiz ve uzun bağlam modeli" },
                new() { Id = "gemini-2.5-flash", Name = "Gemini 2.5 Flash (Antigravity)", Category = "Antigravity Proxy", Description = "Hızlı özetleme ve transkript modeli" },
                new() { Id = "claude-3-7-sonnet", Name = "Claude 3.7 Sonnet (Antigravity)", Category = "Antigravity Proxy", Description = "Yüksek kaliteli metin sentezi" }
            };
        }

        // OpenRouter canlı model listesi (15 dk önbellekli)
        try
        {
            var cached = await _cache.GetAsync(OpenRouterModelsCacheKey);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                var list = JsonSerializer.Deserialize<List<ModelOptionDto>>(cached);
                if (list is { Count: > 0 })
                    return list;
            }
        }
        catch { }

        try
        {
            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var res = await client.GetAsync("https://openrouter.ai/api/v1/models", ct);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                {
                    var liveModels = new List<ModelOptionDto>();
                    foreach (var item in dataArr.EnumerateArray())
                    {
                        var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(id)) continue;

                        var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? id : id;
                        var contextLen = item.TryGetProperty("context_length", out var ctxProp) && ctxProp.ValueKind == JsonValueKind.Number
                            ? ctxProp.GetInt32()
                            : 0;

                        var isFree = id.EndsWith(":free", StringComparison.OrdinalIgnoreCase);
                        var category = ResolveCategory(id, isFree);
                        var ctxText = contextLen >= 1000 ? $"{contextLen / 1000}K ctx" : "";

                        liveModels.Add(new ModelOptionDto
                        {
                            Id = id,
                            Name = isFree && !name.Contains("free", StringComparison.OrdinalIgnoreCase) ? $"{name} (Ücretsiz)" : name,
                            Category = category,
                            Description = string.IsNullOrEmpty(ctxText) ? id : $"{id} • {ctxText}"
                        });
                    }

                    if (liveModels.Count > 0)
                    {
                        var sorted = liveModels
                            .OrderBy(m => GetCategoryPriority(m.Category))
                            .ThenBy(m => m.Name)
                            .ToList();

                        try
                        {
                            await _cache.SetAsync(OpenRouterModelsCacheKey, JsonSerializer.Serialize(sorted), TimeSpan.FromMinutes(15));
                        }
                        catch { }

                        return sorted;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenRouter canlı model listesi alınamadı, yerleşik listeye dönülüyor.");
        }

        return GetFallbackOpenRouterModels();
    }

    private static string NormalizeModelId(string rawModel)
    {
        var trimmed = rawModel.Trim();
        if (trimmed.StartsWith("https://openrouter.ai/", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(trimmed);
            var path = uri.AbsolutePath.Trim('/');
            if (path.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                path = path.Substring("models/".Length);
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }
        return trimmed;
    }

    private static string ResolveCategory(string id, bool isFree)
    {
        var lower = id.ToLowerInvariant();
        if (isFree) return "Ücretsiz Modeller (:free)";
        if (lower.StartsWith("google/")) return "Google Gemini";
        if (lower.StartsWith("anthropic/")) return "Anthropic Claude";
        if (lower.StartsWith("openai/")) return "OpenAI GPT";
        if (lower.StartsWith("deepseek/")) return "DeepSeek";
        if (lower.StartsWith("qwen/")) return "Qwen";
        if (lower.StartsWith("meta-llama/")) return "Meta Llama";
        if (lower.StartsWith("x-ai/")) return "xAI Grok";
        if (lower.StartsWith("mistralai/")) return "Mistral AI";
        return "Diğer Modeller";
    }

    private static int GetCategoryPriority(string category) => category switch
    {
        "Google Gemini" => 1,
        "Ücretsiz Modeller (:free)" => 2,
        "Anthropic Claude" => 3,
        "OpenAI GPT" => 4,
        "DeepSeek" => 5,
        "Qwen" => 6,
        "xAI Grok" => 7,
        "Meta Llama" => 8,
        "Mistral AI" => 9,
        _ => 10
    };

    private static List<ModelOptionDto> GetFallbackOpenRouterModels() => new()
    {
        new() { Id = "google/gemini-2.5-flash", Name = "Gemini 2.5 Flash", Category = "Google Gemini", Description = "Hızlı, ekonomik ve ses/metin destekli" },
        new() { Id = "google/gemini-2.5-pro", Name = "Gemini 2.5 Pro", Category = "Google Gemini", Description = "En yüksek doğruluk ve 1M bağlam" },
        new() { Id = "google/gemini-2.0-flash-001", Name = "Gemini 2.0 Flash", Category = "Google Gemini", Description = "Düşük gecikmeli multimodal model" },
        new() { Id = "anthropic/claude-3.7-sonnet", Name = "Claude 3.7 Sonnet", Category = "Anthropic Claude", Description = "Akademik metin ve senaryo üretiminde lider" },
        new() { Id = "anthropic/claude-3.5-sonnet", Name = "Claude 3.5 Sonnet", Category = "Anthropic Claude", Description = "Yüksek kaliteli içerik ve kod sentezi" },
        new() { Id = "openai/gpt-4o", Name = "GPT-4o", Category = "OpenAI GPT", Description = "OpenAI amiral gemisi multimodal model" },
        new() { Id = "openai/gpt-4o-mini", Name = "GPT-4o Mini", Category = "OpenAI GPT", Description = "Hızlı ve düşük maliyetli" },
        new() { Id = "deepseek/deepseek-chat-v3-0324:free", Name = "DeepSeek V3 (Ücretsiz)", Category = "Ücretsiz Modeller (:free)", Description = "Güçlü genel amaçlı ücretsiz model" },
        new() { Id = "deepseek/deepseek-r1:free", Name = "DeepSeek R1 (Ücretsiz)", Category = "Ücretsiz Modeller (:free)", Description = "Muhakeme odaklı ücretsiz model" }
    };
}
