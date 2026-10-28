using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class GeminiSummarizer : IGeminiProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiSummarizer> _logger;
    private readonly IAiProviderSettingsService? _settingsService;
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    private static int _currentKeyIndex = 0;

    // Proxy'ye gönderilen model; kayıtlardaki LlmModel alanı da buradan beslenir.
    private const string ModelName = "gemini-3.8-flash-tiered";

    public string ActiveModelName
    {
        get
        {
            if (_settingsService != null)
            {
                try
                {
                    var settings = _settingsService.GetSettingsAsync().GetAwaiter().GetResult();
                    if (settings.ActiveProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
                    {
                        return string.IsNullOrWhiteSpace(settings.OpenRouterModel) ? "google/gemini-3.8-flash" : settings.OpenRouterModel;
                    }
                    if (settings.ActiveProvider.Equals("groq", StringComparison.OrdinalIgnoreCase))
                    {
                        return string.IsNullOrWhiteSpace(settings.GroqModel) ? "openai/gpt-oss-120b" : settings.GroqModel;
                    }
                    if (!string.IsNullOrWhiteSpace(settings.AntigravityProxyModel))
                    {
                        return settings.AntigravityProxyModel;
                    }
                }
                catch { }
            }
            return ModelName;
        }
    }

    public GeminiSummarizer(
        IConfiguration configuration,
        ILogger<GeminiSummarizer> logger)
        : this(configuration, logger, null)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public GeminiSummarizer(
        IConfiguration configuration,
        ILogger<GeminiSummarizer> logger,
        IAiProviderSettingsService? settingsService)
    {
        _configuration = configuration;
        _logger = logger;
        _settingsService = settingsService;
    }

    public async Task<string> SummarizeAsync(string transcript, string modelName = "gemini-1.5-flash")
    {
        AiProviderSettingsDto? settings = null;
        if (_settingsService != null)
        {
            try
            {
                settings = await _settingsService.GetSettingsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI Provider ayarları okunamadı, varsayılan yapılandırma kullanılacak.");
            }
        }

        var prompt = $@"
        Sen bir bilgi çıkarma ve rafinasyon uzmanısın. Görevin aşağıdaki metni klasik anlamda 'özetlemek' değil, 'damıtmak'tır. Aşağıdaki kurallara kesinlikle uy: 
        1) Konuyla ilgili verilen TÜM bilgileri, iddiaları, kuralları ve teknik detayları %100 oranında koru. Hiçbir bilgi kırıntısını atlama. 
        2) Konuşmacının kendini tekrar ettiği yerleri, konudan tamamen bağımsız anılarını/sohbetlerini ve 'ııı, eee, yani' gibi boş laflarını tamamen temizle. 
        3) Çıktın, asıl metnin bilgi yoğunluğunu kaybetmeden sadece gereksiz tekrarlardan ve konu dışı gürültüden arındırılmış, saf ve akıcı bir bilgi dökümü olmalıdır.

        Lütfen cevabı sadece geçerli bir JSON formatında döndür.
        
        İstenen JSON formatı:
        {{
            ""ozetMetni"": ""Damıtılmış, eksiksiz ancak gereksiz tekrarlardan arındırılmış tam metin"",
            ""konuBasliklari"": [""Başlık 1"", ""Başlık 2""],
            ""konuEtiketleri"": [""etiket1"", ""etiket2""]
        }}

        Transkript:
        {transcript}
        ";

        // 1. Eğer aktif sağlayıcı "groq" ise doğrudan kullanıcının girdiği Groq API anahtarını ve modelini kullan
        if (settings != null && settings.ActiveProvider.Equals("groq", StringComparison.OrdinalIgnoreCase))
        {
            var groqDirectKey = !string.IsNullOrWhiteSpace(settings.GroqApiKey)
                ? settings.GroqApiKey
                : (_configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"]);

            if (string.IsNullOrWhiteSpace(groqDirectKey))
            {
                throw new Exception("Groq API seçili ancak Groq API Anahtarı girilmemiş. Lütfen API Ayarları menüsünden Groq anahtarınızı girin.");
            }

            var preferredModel = string.IsNullOrWhiteSpace(settings.GroqModel) ? "openai/gpt-oss-120b" : settings.GroqModel;
            var candidateModels = new[] { preferredModel, "openai/gpt-oss-20b", "qwen/qwen3.8-27b", "openai/gpt-oss-120b" }
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return await CallGroqModelsAsync(groqDirectKey, candidateModels, prompt, null);
        }

        bool isOpenRouter = settings != null &&
                            settings.ActiveProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase);

        var apiKeyStr = isOpenRouter
            ? settings!.OpenRouterApiKey
            : (settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyKey)
                ? settings.AntigravityProxyKey
                : _configuration["GEMINI_API_KEY"]);

        if (string.IsNullOrWhiteSpace(apiKeyStr))
        {
            // Eğer OpenRouter anahtarı girilmemiş ama Groq anahtarı girilmişse kullanıcının Groq anahtarını kullan
            var userGroqKey = settings != null && !string.IsNullOrWhiteSpace(settings.GroqApiKey)
                ? settings.GroqApiKey
                : (_configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"]);

            if (!string.IsNullOrWhiteSpace(userGroqKey))
            {
                var preferred = settings != null && !string.IsNullOrWhiteSpace(settings.GroqModel) ? settings.GroqModel : "openai/gpt-oss-120b";
                return await CallGroqModelsAsync(userGroqKey, new[] { preferred, "openai/gpt-oss-20b", "qwen/qwen3.8-27b", "openai/gpt-oss-120b" }.Distinct().ToArray(), prompt, null);
            }

            if (_settingsService == null)
            {
                _logger.LogWarning("GEMINI_API_KEY is not set. Using mock summary for development.");
                return "{\"ozetMetni\":\"Mock Özet\", \"konuBasliklari\":[\"Mock Baslik\"], \"konuEtiketleri\":[\"mock\", \"test\"]}";
            }

            throw new Exception("Seçili yapay zeka sağlayıcısı için API anahtarı girilmemiş! Lütfen sağ üstteki 'API Ayarları' menüsünden API anahtarınızı girip kaydedin.");
        }

        var apiKeys = apiKeyStr.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(k => k.Trim())
                               .ToArray();

        var effectiveModel = isOpenRouter
            ? (string.IsNullOrWhiteSpace(settings!.OpenRouterModel) ? "google/gemini-3.8-flash" : settings.OpenRouterModel)
            : (settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyModel)
                ? settings.AntigravityProxyModel
                : ModelName);

        var baseUrl = isOpenRouter
            ? settings!.OpenRouterBaseUrl.TrimEnd('/')
            : (settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyUrl)
                ? settings.AntigravityProxyUrl.TrimEnd('/')
                : "http://localhost:8045/v1");

        var requestBody = new
        {
            model = effectiveModel,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = 16384,
            temperature = 0.2
        };

        Exception? lastException = null;

        for (int tryCount = 0; tryCount < apiKeys.Length; tryCount++)
        {
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var keyToUse = apiKeys[_currentKeyIndex % apiKeys.Length];
            try
            {
                var requestUrl = $"{baseUrl}/chat/completions";
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                requestMessage.Headers.Add("Authorization", $"Bearer {keyToUse}");
                if (isOpenRouter)
                {
                    requestMessage.Headers.Add("HTTP-Referer", "https://videoozet.local");
                    requestMessage.Headers.Add("X-Title", "VideoOzet");
                }
                requestMessage.Content = jsonContent;
                
                var response = await _httpClient.SendAsync(requestMessage);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (responseString.Contains("429") || responseString.Contains("quota") || responseString.Contains("exhausted"))
                    {
                        throw new Exception($"Quota Exceeded: {responseString}");
                    }
                    else if (responseString.Contains("503") || responseString.Contains("UNAVAILABLE") || responseString.Contains("high demand"))
                    {
                        throw new Exception($"High Demand: {responseString}");
                    }
                    else
                    {
                        throw new Exception($"API error: {response.StatusCode} - {responseString}");
                    }
                }

                using var jsonDoc = JsonDocument.Parse(responseString);
                var root = jsonDoc.RootElement;
                
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }

                throw new Exception($"Unexpected API response format: {responseString}");
            }
            catch (Exception ex) when (ex.Message.Contains("Quota Exceeded") || ex.Message.Contains("High Demand") || ex.Message.Contains("exhausted"))
            {
                _logger.LogWarning("API Key {Key} hit limit or high demand on proxy. Rotating... Error: {Error}", keyToUse.Substring(0, Math.Min(keyToUse.Length, 5)) + "...", ex.Message);
                System.Threading.Interlocked.Increment(ref _currentKeyIndex);
                lastException = ex;
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning("Primary LLM ({BaseUrl}) failed during SummarizeAsync: {Message}.", baseUrl, ex.Message);
                break;
            }
        }

        // Eğer kullanıcı arayüzden ayrıca bir Groq API Anahtarı girdiyse, yedek olarak kullanıcının Groq anahtarını kullan
        var groqKey = settings != null && !string.IsNullOrWhiteSpace(settings.GroqApiKey)
            ? settings.GroqApiKey
            : (_configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"]);

        if (!string.IsNullOrWhiteSpace(groqKey))
        {
            var preferred = settings != null && !string.IsNullOrWhiteSpace(settings.GroqModel) ? settings.GroqModel : "openai/gpt-oss-120b";
            var models = new[] { preferred, "openai/gpt-oss-20b", "qwen/qwen3.8-27b", "openai/gpt-oss-120b" }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return await CallGroqModelsAsync(groqKey, models, prompt, lastException);
        }

        throw new Exception("Seçili API anahtarı hata verdi veya kullanılamıyor! Lütfen 'API Ayarları' menüsünden girdiğiniz API anahtarını kontrol edin.", lastException);
    }

    private async Task<string> CallGroqModelsAsync(string groqKey, string[] candidateModels, string prompt, Exception? lastException)
    {
        var allModels = candidateModels
            .Concat(new[] { "openai/gpt-oss-20b", "qwen/qwen3.8-27b", "openai/gpt-oss-120b" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        int[] maxCharsPasses = { 9500, 7200 };
        for (int pass = 0; pass < maxCharsPasses.Length; pass++)
        {
            if (pass > 0)
            {
                _logger.LogInformation("Groq TPM penceresi için 14 saniye beklenip sıkıştırılmış bağlamla tekrar deneniyor...");
                await Task.Delay(14000);
            }

            var groqPrompt = CompactPromptForGroq(prompt, maxCharsPasses[pass]);
            foreach (var groqModel in allModels)
            {
                try
                {
                    _logger.LogInformation("Groq Cloud LLM ({Model}) ile özetleme yapılıyor (Prompt: {Len} krk)...", groqModel, groqPrompt.Length);
                    var groqBody = new
                    {
                        model = groqModel,
                        messages = new[] { new { role = "user", content = groqPrompt } },
                        max_tokens = 8192,
                        temperature = 0.1
                    };
                    using var groqReq = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                    groqReq.Headers.Add("Authorization", $"Bearer {groqKey.Trim()}");
                    groqReq.Headers.Add("User-Agent", "VideoOzet/1.0");
                    groqReq.Content = new StringContent(JsonSerializer.Serialize(groqBody), Encoding.UTF8, "application/json");

                    var groqResp = await _httpClient.SendAsync(groqReq);
                    var groqStr = await groqResp.Content.ReadAsStringAsync();
                    if (groqResp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(groqStr);
                        if (doc.RootElement.TryGetProperty("choices", out var ch) && ch.GetArrayLength() > 0 &&
                            ch[0].TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var cnt))
                        {
                            var contentStr = cnt.GetString();
                            if (!string.IsNullOrWhiteSpace(contentStr))
                                return contentStr;
                        }
                    }
                    else
                    {
                        lastException = new Exception($"Groq API error ({groqResp.StatusCode}): {groqStr}");
                        _logger.LogWarning("Groq LLM ({Model}) returned {Status}: {Body}", groqModel, groqResp.StatusCode, groqStr);
                    }
                }
                catch (Exception gEx)
                {
                    lastException = gEx;
                    _logger.LogWarning(gEx, "Groq LLM ({Model}) çağrısı başarısız oldu.", groqModel);
                }
            }
        }

        throw new Exception("Groq API çağrısı başarısız oldu! Lütfen API Ayarları menüsünden girdiğiniz Groq API anahtarını kontrol edin.", lastException);
    }

    private static string CompactPromptForGroq(string prompt, int maxChars)
    {
        if (string.IsNullOrEmpty(prompt) || prompt.Length <= maxChars)
            return prompt;

        int headChars = (int)(maxChars * 0.60);
        int tailChars = maxChars - headChars;

        return prompt.Substring(0, headChars)
             + "\n\n[... Uzun metin token limiti nedeniyle kısaltıldı ...]\n\n"
             + prompt.Substring(prompt.Length - tailChars);
    }
}
