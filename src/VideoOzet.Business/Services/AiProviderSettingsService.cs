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
    private static readonly string SharedFilePath = Path.Combine(AppContext.BaseDirectory, "ai_provider_settings.json");

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
                    cached.OpenRouterModel = NormalizeModelId(cached.OpenRouterModel);
                    _memoryFallback = cached;
                    return cached;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis üzerinden AI Provider ayarları okunamadı, yerel dosya/bellek yedeğine geçiliyor.");
        }

        // Yerel dosya yedeği (Uygulama klasöründeki ai_provider_settings.json)
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
                        fromFile.OpenRouterModel = NormalizeModelId(fromFile.OpenRouterModel);
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
            ActiveProvider = _config["AI_ACTIVE_PROVIDER"] ?? "openrouter",
            OpenRouterApiKey = _config["OpenRouter:ApiKey"] ?? _config["OPENROUTER_API_KEY"] ?? "",
            OpenRouterBaseUrl = _config["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1",
            OpenRouterModel = _config["OpenRouter:Model"] ?? "google/gemini-3.8-flash",
            GroqApiKey = _config["GROQ_API_KEY"] ?? _config["Groq:ApiKey"] ?? "",
            GroqModel = _config["GROQ_MODEL"] ?? "openai/gpt-oss-120b",
            AntigravityProxyUrl = _config["Antigravity:ProxyUrl"] ?? "http://localhost:8045/v1",
            AntigravityProxyKey = _config["GEMINI_API_KEY"] ?? "",
            AntigravityProxyModel = _config["GEMINI_MODEL"] ?? "gemini-3.8-flash-tiered"
        };

        return defaultSettings;
    }

    public async Task<AiProviderSettingsDto> UpdateSettingsAsync(AiProviderSettingsDto settings, CancellationToken ct = default)
    {
        var current = await GetSettingsAsync(ct);

        var updated = new AiProviderSettingsDto
        {
            ActiveProvider = string.IsNullOrWhiteSpace(settings.ActiveProvider) ? current.ActiveProvider : settings.ActiveProvider.Trim().ToLowerInvariant(),
            OpenRouterApiKey = (settings.OpenRouterApiKey ?? "").Trim(),
            OpenRouterBaseUrl = string.IsNullOrWhiteSpace(settings.OpenRouterBaseUrl) ? current.OpenRouterBaseUrl : settings.OpenRouterBaseUrl.Trim(),
            OpenRouterModel = string.IsNullOrWhiteSpace(settings.OpenRouterModel) ? current.OpenRouterModel : NormalizeModelId(settings.OpenRouterModel),
            GroqApiKey = (settings.GroqApiKey ?? "").Trim(),
            GroqModel = string.IsNullOrWhiteSpace(settings.GroqModel) ? current.GroqModel : settings.GroqModel.Trim(),
            AntigravityProxyUrl = string.IsNullOrWhiteSpace(settings.AntigravityProxyUrl) ? current.AntigravityProxyUrl : settings.AntigravityProxyUrl.Trim(),
            AntigravityProxyKey = (settings.AntigravityProxyKey ?? "").Trim(),
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

        _logger.LogInformation("AI Provider ayarları güncellendi. Aktif Sağlayıcı: {Provider}", updated.ActiveProvider);

        return updated;
    }

    public async Task<TestConnectionResultDto> TestConnectionAsync(AiProviderSettingsDto settings, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            if (settings.ActiveProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
                {
                    return new TestConnectionResultDto
                    {
                        Success = false,
                        Message = "Lütfen OpenRouter API anahtarınızı girin.",
                        LatencyMs = 0
                    };
                }

                var url = $"{settings.OpenRouterBaseUrl.TrimEnd('/')}/auth/key";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenRouterApiKey.Trim());

                var res = await client.SendAsync(req, ct);
                sw.Stop();

                if (res.IsSuccessStatusCode)
                {
                    var sttNote = !string.IsNullOrWhiteSpace(settings.GroqApiKey)
                        ? " + Groq Whisper STT anahtarı tanımlı"
                        : "";
                    return new TestConnectionResultDto
                    {
                        Success = true,
                        Message = $"OpenRouter bağlantısı başarılı! Model: {NormalizeModelId(settings.OpenRouterModel)}{sttNote}",
                        LatencyMs = sw.ElapsedMilliseconds
                    };
                }

                return new TestConnectionResultDto
                {
                    Success = false,
                    Message = $"OpenRouter API anahtarı geçersiz veya hata döndü (HTTP {(int)res.StatusCode}). Lütfen geçerli bir OpenRouter anahtarı girin.",
                    LatencyMs = sw.ElapsedMilliseconds
                };
            }
            else if (settings.ActiveProvider.Equals("groq", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(settings.GroqApiKey))
                {
                    return new TestConnectionResultDto
                    {
                        Success = false,
                        Message = "Lütfen Groq API anahtarınızı (gsk_...) girin.",
                        LatencyMs = 0
                    };
                }

                using var groqReq = new HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/openai/v1/models");
                groqReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.GroqApiKey.Trim());
                var groqRes = await client.SendAsync(groqReq, ct);
                sw.Stop();

                if (groqRes.IsSuccessStatusCode)
                {
                    return new TestConnectionResultDto
                    {
                        Success = true,
                        Message = $"Groq Cloud API (Whisper STT + {settings.GroqModel}) bağlantısı başarılı!",
                        LatencyMs = sw.ElapsedMilliseconds
                    };
                }

                return new TestConnectionResultDto
                {
                    Success = false,
                    Message = $"Groq API anahtarı geçersiz veya hata döndü (HTTP {(int)groqRes.StatusCode}).",
                    LatencyMs = sw.ElapsedMilliseconds
                };
            }
            else
            {
                if (string.IsNullOrWhiteSpace(settings.AntigravityProxyUrl))
                {
                    return new TestConnectionResultDto
                    {
                        Success = false,
                        Message = "Lütfen Proxy URL adresini girin.",
                        LatencyMs = 0
                    };
                }

                var url = $"{settings.AntigravityProxyUrl.TrimEnd('/')}/models";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(settings.AntigravityProxyKey))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AntigravityProxyKey.Trim());
                }

                var res = await client.SendAsync(req, ct);
                sw.Stop();

                if (res.IsSuccessStatusCode)
                {
                    return new TestConnectionResultDto
                    {
                        Success = true,
                        Message = $"Yerel / Özel Proxy bağlantısı aktif! Seçili model: {settings.AntigravityProxyModel}",
                        LatencyMs = sw.ElapsedMilliseconds
                    };
                }

                return new TestConnectionResultDto
                {
                    Success = false,
                    Message = $"Proxy sunucusu hata döndürdü: HTTP {(int)res.StatusCode}",
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
        if (provider.Equals("groq", StringComparison.OrdinalIgnoreCase))
        {
            return new List<ModelOptionDto>
            {
                new() { Id = "openai/gpt-oss-120b", Name = "GPT-OSS 120B (Groq LPU - Önerilen)", Category = "Groq Cloud", Description = "128K bağlam, ultra hızlı Türkçe özetleme ve senaryo üretimi" },
                new() { Id = "llama-3.3-70b-versatile", Name = "Llama 3.3 70B Versatile (Groq LPU)", Category = "Groq Cloud", Description = "128K bağlam, yüksek TPM kapasiteli çok dilli model" },
                new() { Id = "openai/gpt-oss-20b", Name = "GPT-OSS 20B (Groq LPU - Hızlı)", Category = "Groq Cloud", Description = "128K bağlam, çok yüksek hız ve düşük token tüketimi" },
                new() { Id = "qwen/qwen3.8-27b", Name = "Qwen 3.8 27B (Groq LPU)", Category = "Groq Cloud", Description = "128K bağlam, muhakeme destekli çok dilli model" }
            };
        }

        if (provider.Equals("antigravity", StringComparison.OrdinalIgnoreCase))
        {
            var settings = await GetSettingsAsync(ct);
            if (settings.ActiveProvider.Equals("antigravity", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(settings.AntigravityProxyKey) &&
                !string.IsNullOrWhiteSpace(settings.AntigravityProxyUrl))
            {
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
                        if (string.IsNullOrWhiteSpace(id) || id.EndsWith(":batch", StringComparison.OrdinalIgnoreCase) || id.StartsWith("~")) continue;

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
                            .ThenBy(m => m.Id.Equals("google/gemini-3.8-flash", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                            .ThenByDescending(m => m.Id.StartsWith("google/gemini-3.", StringComparison.OrdinalIgnoreCase))
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
        if (string.IsNullOrWhiteSpace(rawModel))
            return "google/gemini-3.8-flash";

        var trimmed = rawModel.Trim().TrimStart('~').Replace(":batch", "", StringComparison.OrdinalIgnoreCase);
        if (trimmed.StartsWith("https://openrouter.ai/", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(trimmed);
            var path = uri.AbsolutePath.Trim('/');
            if (path.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                path = path.Substring("models/".Length);
            if (!string.IsNullOrWhiteSpace(path))
                trimmed = path.Replace(":batch", "", StringComparison.OrdinalIgnoreCase);
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
        new() { Id = "google/gemini-3.8-flash", Name = "Google: Gemini 3.8 Flash (Varsayılan)", Category = "Google Gemini", Description = "En güncel yüksek hızlı Gemini 3.8 modeli" },
        new() { Id = "google/gemini-3.7-flash", Name = "Google: Gemini 3.7 Flash", Category = "Google Gemini", Description = "Yüksek hızlı Gemini 3.7 modeli" },
        new() { Id = "google/gemini-3.5-flash", Name = "Google: Gemini 3.5 Flash", Category = "Google Gemini", Description = "Hızlı ve dengeli Gemini 3.5 modeli" },
        new() { Id = "google/gemini-2.5-flash", Name = "Gemini 2.5 Flash", Category = "Google Gemini", Description = "Hızlı, ekonomik ve ses/metin destekli" },
        new() { Id = "google/gemini-2.5-pro", Name = "Gemini 2.5 Pro", Category = "Google Gemini", Description = "En yüksek doğruluk ve 1M bağlam" },
        new() { Id = "anthropic/claude-3.7-sonnet", Name = "Claude 3.7 Sonnet", Category = "Anthropic Claude", Description = "Akademik metin ve senaryo üretiminde lider" },
        new() { Id = "anthropic/claude-3.5-sonnet", Name = "Claude 3.5 Sonnet", Category = "Anthropic Claude", Description = "Yüksek kaliteli içerik ve kod sentezi" },
        new() { Id = "openai/gpt-4o", Name = "GPT-4o", Category = "OpenAI GPT", Description = "OpenAI amiral gemisi multimodal model" },
        new() { Id = "openai/gpt-4o-mini", Name = "GPT-4o Mini", Category = "OpenAI GPT", Description = "Hızlı ve düşük maliyetli" },
        new() { Id = "deepseek/deepseek-chat-v3-0324:free", Name = "DeepSeek V3 (Ücretsiz)", Category = "Ücretsiz Modeller (:free)", Description = "Güçlü genel amaçlı ücretsiz model" },
        new() { Id = "deepseek/deepseek-r1:free", Name = "DeepSeek R1 (Ücretsiz)", Category = "Ücretsiz Modeller (:free)", Description = "Muhakeme odaklı ücretsiz model" }
    };
}
