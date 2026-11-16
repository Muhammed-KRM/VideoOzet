using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class GeminiSummarizer : IGeminiProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiSummarizer> _logger;
    private readonly IAiProviderSettingsService? _settingsService;
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
    private static int _currentKeyIndex = 0;

    // Proxy'ye gönderilen model; kayıtlardaki LlmModel alanı da buradan beslenir.
    private const string ModelName = "gemini-3.8-flash-tiered";
    private const int MaxTranscriptSectionChars = 20000;

    private static readonly JsonSerializerOptions UnescapedJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Regex EnglishStopWordsRegex = new(
        @"\b(the|and|that|this|with|from|have|for|not|are|was|were|will|would|could|should|about|into|which|their|there|been|because|between|through|where|when|while|these|those)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TurkishStopWordsRegex = new(
        @"\b(ve|bir|bu|için|ile|olan|olarak|daha|çok|ama|fakat|ancak|gibi|kadar|sonra|önce|üzerine|arasında|tarafından|nedir|nasıl|değil|var|yok|veya|hem|ise)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GermanStopWordsRegex = new(
        @"\b(der|die|das|und|den|von|mit|sich|auf|für|nicht|ist|dem|auch|werden|aus|hat|dass|wir|ein|eine|einer|einem)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string ActiveModelName
    {
        get
        {
            if (_settingsService != null)
            {
                try
                {
                    var settings = _settingsService.GetSettingsAsync().GetAwaiter().GetResult();
                    return settings.GetEffectiveSummaryModel();
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
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return JsonSerializer.Serialize(new
            {
                dil = "tr",
                ozetMetni = string.Empty,
                konuBasliklari = Array.Empty<string>(),
                konuEtiketleri = Array.Empty<string>()
            });
        }

        if (transcript.Length <= MaxTranscriptSectionChars)
        {
            var singleRaw = await SummarizeSingleSectionRawAsync(transcript);
            return EnsureValidSummaryJson(singleRaw, transcript);
        }

        var sections = SplitIntoSemanticSections(transcript, MaxTranscriptSectionChars);
        _logger.LogInformation(
            "Uzun metin ({TotalChars} krk) {SectionCount} anlamlı parçaya bölünerek damıtılıyor...",
            transcript.Length, sections.Count);

        var distilledParts = new List<string>(sections.Count);
        var allHeadings = new List<string>();
        var allTags = new List<string>();
        string? detectedFromLlm = null;

        for (int i = 0; i < sections.Count; i++)
        {
            var rawSectionResult = await SummarizeSingleSectionRawAsync(sections[i]);
            var parsed = TryParseOrRecoverSummary(rawSectionResult, sections[i]);

            if (!string.IsNullOrWhiteSpace(parsed.Dil) && string.IsNullOrWhiteSpace(detectedFromLlm))
            {
                detectedFromLlm = parsed.Dil;
            }

            if (!string.IsNullOrWhiteSpace(parsed.OzetMetni))
            {
                distilledParts.Add(parsed.OzetMetni.Trim());
            }

            if (parsed.KonuBasliklari != null)
            {
                foreach (var h in parsed.KonuBasliklari)
                {
                    if (!string.IsNullOrWhiteSpace(h) && !allHeadings.Contains(h.Trim(), StringComparer.OrdinalIgnoreCase))
                        allHeadings.Add(h.Trim());
                }
            }

            if (parsed.KonuEtiketleri != null)
            {
                foreach (var t in parsed.KonuEtiketleri)
                {
                    if (!string.IsNullOrWhiteSpace(t) && !allTags.Contains(t.Trim(), StringComparer.OrdinalIgnoreCase))
                        allTags.Add(t.Trim());
                }
            }
        }

        var finalLang = DetectLanguageReliably(transcript, string.Join("\n\n", distilledParts), detectedFromLlm);
        var combinedSummary = string.Join("\n\n", distilledParts);

        return JsonSerializer.Serialize(new
        {
            dil = finalLang,
            ozetMetni = combinedSummary,
            konuBasliklari = allHeadings,
            konuEtiketleri = allTags
        }, UnescapedJsonOptions);
    }

    private async Task<string> SummarizeSingleSectionRawAsync(string transcriptSection)
    {
        var prompt = $@"
        Sen bir bilgi çıkarma ve rafinasyon uzmanısın. Görevin aşağıdaki metni klasik anlamda 'özetlemek' değil, 'damıtmak'tır. Aşağıdaki kurallara kesinlikle uy: 
        1) Metin hangi dilde ise 'ozetMetni' alanını O ORİJİNAL DİLDE damıt (bilgi kaybı yaşamamak için bu adımda çeviri yapma).
        2) Konuyla ilgili verilen TÜM bilgileri, iddiaları, düşünürleri, verileri, kuralları ve teknik detayları %100 oranında koru. Hiçbir bilgi kırıntısını atlama. 
        3) Konuşmacının kendini tekrar ettiği yerleri, konudan tamamen bağımsız anılarını/sohbetlerini ve 'ııı, eee, yani' gibi boş laflarını tamamen temizle. 
        4) Çıktın, asıl metnin bilgi yoğunluğunu kaybetmeden sadece gereksiz tekrarlardan ve konu dışı gürültüden arındırılmış, saf ve akıcı bir bilgi dökümü olmalıdır.
        5) 'dil' alanında metnin ISO dil kodunu (örn: 'tr', 'en', 'de', 'fr' vb.) belirt.
        6) 'konuBasliklari' ve 'konuEtiketleri' alanlarını ise her zaman Türkçe olarak yaz.

        Lütfen cevabı sadece geçerli bir JSON formatında döndür.
        
        İstenen JSON formatı:
        {{
            ""dil"": ""tr"",
            ""ozetMetni"": ""Damıtılmış, eksiksiz ancak gereksiz tekrarlardan arındırılmış tam metin"",
            ""konuBasliklari"": [""Başlık 1"", ""Başlık 2""],
            ""konuEtiketleri"": [""etiket1"", ""etiket2""]
        }}

        Transkript:
        {transcriptSection}
        ";

        return await ExecuteLlmPromptAsync(prompt);
    }

    public async Task<string> TranslateDistilledSummaryToTurkishAsync(string text, string sourceLang = "en")
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (text.Length <= MaxTranscriptSectionChars)
        {
            var prompt = string.Format(PromptTemplates.TermPreservingTranslationPrompt, sourceLang, text);
            var translated = await ExecuteLlmPromptAsync(prompt);
            return translated.Trim();
        }

        var sections = SplitIntoSemanticSections(text, MaxTranscriptSectionChars);
        var translatedParts = new List<string>(sections.Count);
        foreach (var section in sections)
        {
            var prompt = string.Format(PromptTemplates.TermPreservingTranslationPrompt, sourceLang, section);
            var partTranslated = await ExecuteLlmPromptAsync(prompt);
            if (!string.IsNullOrWhiteSpace(partTranslated))
            {
                translatedParts.Add(partTranslated.Trim());
            }
        }

        return string.Join("\n\n", translatedParts);
    }

    private async Task<string> ExecuteLlmPromptAsync(string prompt)
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

        bool allowFallback = settings?.AllowFallbackToSecondaryProvider ?? false;

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
            if (_settingsService == null)
            {
                _logger.LogWarning("GEMINI_API_KEY is not set. Using mock summary for development.");
                return "{\"ozetMetni\":\"Mock Özet\", \"konuBasliklari\":[\"Mock Baslik\"], \"konuEtiketleri\":[\"mock\", \"test\"]}";
            }

            if (allowFallback)
            {
                var userGroqKey = !string.IsNullOrWhiteSpace(settings?.GroqApiKey)
                    ? settings!.GroqApiKey
                    : (_configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"]);

                if (!string.IsNullOrWhiteSpace(userGroqKey))
                {
                    var preferred = !string.IsNullOrWhiteSpace(settings?.GroqModel) ? settings!.GroqModel : "openai/gpt-oss-120b";
                    return await CallGroqModelsAsync(
                        userGroqKey,
                        new[] { preferred, "openai/gpt-oss-20b", "qwen/qwen3.8-27b", "openai/gpt-oss-120b" }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        prompt,
                        null);
                }
            }

            throw new Exception("Seçili yapay zeka sağlayıcısı için API anahtarı girilmemiş! Lütfen sağ üstteki 'API Ayarları' menüsünden API anahtarınızı girip kaydedin.");
        }

        var apiKeys = apiKeyStr.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(k => k.Trim())
                               .ToArray();

        var effectiveModel = settings != null
            ? settings.GetEffectiveSummaryModel()
            : ModelName;

        var baseUrl = isOpenRouter
            ? settings!.OpenRouterBaseUrl.TrimEnd('/')
            : (settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyUrl)
                ? settings.AntigravityProxyUrl.TrimEnd('/')
                : "http://localhost:8045/v1");

        bool isReasoningModel = effectiveModel.Contains("r1", StringComparison.OrdinalIgnoreCase) ||
                                effectiveModel.Contains("o1", StringComparison.OrdinalIgnoreCase) ||
                                effectiveModel.Contains("o3", StringComparison.OrdinalIgnoreCase) ||
                                effectiveModel.Contains("reasoning", StringComparison.OrdinalIgnoreCase) ||
                                effectiveModel.Contains("thinking", StringComparison.OrdinalIgnoreCase);

        int maxRetries = Math.Max(apiKeys.Length, 3);
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var keyToUse = apiKeys[_currentKeyIndex % apiKeys.Length];
            try
            {
                var (contentResult, finishReason) = await SendChatCompletionRequestAsync(
                    baseUrl,
                    keyToUse,
                    effectiveModel,
                    isOpenRouter,
                    isReasoningModel,
                    new object[] { new { role = "user", content = prompt } });

                if (!string.IsNullOrEmpty(contentResult) &&
                    (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(finishReason, "max_tokens", StringComparison.OrdinalIgnoreCase)))
                {
                    var fullBuilder = new StringBuilder(contentResult);
                    string lastAssistantSegment = contentResult;

                    for (int cont = 1; cont <= 2; cont++)
                    {
                        _logger.LogInformation(
                            "Özetleme yanıtı çıktı token sınırına (finish_reason={Reason}) çarptı. Devam çağrısı ({Cont}/2) yapılıyor...",
                            finishReason, cont);

                        var contMessages = new object[]
                        {
                            new { role = "user", content = prompt },
                            new { role = "assistant", content = fullBuilder.ToString() },
                            new
                            {
                                role = "user",
                                content = "Yanıtın çıktı token sınırına takıldığı için yarım kaldı. Kaldığın tam karakterden/kelimeden itibaren HİÇBİR giriş cümlesi veya tekrar yapmadan doğrudan devam et ve açık kalan JSON/metin yapısını eksiksiz kapat."
                            }
                        };

                        var (contText, contFinishReason) = await SendChatCompletionRequestAsync(
                            baseUrl,
                            keyToUse,
                            effectiveModel,
                            isOpenRouter,
                            isReasoningModel,
                            contMessages);

                        if (string.IsNullOrWhiteSpace(contText) || contText == lastAssistantSegment)
                            break;

                        fullBuilder.Append(contText);
                        lastAssistantSegment = contText;

                        if (!string.Equals(contFinishReason, "length", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(contFinishReason, "max_tokens", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }

                    return fullBuilder.ToString();
                }

                return contentResult;
            }
            catch (Exception ex)
            {
                lastException = ex;
                System.Threading.Interlocked.Increment(ref _currentKeyIndex);

                _logger.LogWarning(
                    "Seçili sağlayıcı ({BaseUrl} / Model: {Model}) deneme {Attempt}/{MaxRetries} sırasında hata verdi: {Error}",
                    baseUrl, effectiveModel, attempt, maxRetries, ex.Message);

                if (attempt < maxRetries)
                {
                    int backoffSeconds = (int)Math.Pow(2, attempt); // 2s, 4s, 8s
                    await Task.Delay(TimeSpan.FromSeconds(backoffSeconds));
                }
            }
        }

        if (!allowFallback)
        {
            var providerLabel = isOpenRouter ? "OpenRouter" : "Yerel Proxy (Antigravity)";
            throw new Exception(
                $"Seçili yapay zeka sağlayıcısı ({providerLabel} - Model: {effectiveModel}) {maxRetries} deneme sonunda yanıt veremedi ve ikincil sağlayıcıya düşüş kapalı (AllowFallbackToSecondaryProvider=false). Son hata: {lastException?.Message}",
                lastException);
        }

        // Yalnızca AllowFallbackToSecondaryProvider == true ise Groq yedeğine düş
        var groqKey = settings != null && !string.IsNullOrWhiteSpace(settings.GroqApiKey)
            ? settings.GroqApiKey
            : (_configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"]);

        if (!string.IsNullOrWhiteSpace(groqKey))
        {
            _logger.LogWarning("AllowFallbackToSecondaryProvider=true olduğu için yedek Groq sağlayıcısına geçiliyor...");
            var preferred = settings != null && !string.IsNullOrWhiteSpace(settings.GroqModel) ? settings.GroqModel : "openai/gpt-oss-120b";
            var models = new[] { preferred, "openai/gpt-oss-20b", "qwen/qwen3.8-27b", "openai/gpt-oss-120b" }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return await CallGroqModelsAsync(groqKey, models, prompt, lastException);
        }

        throw new Exception(
            $"Seçili API anahtarı ({effectiveModel}) {maxRetries} deneme sonunda hata verdi veya kullanılamıyor! Lütfen 'API Ayarları' menüsünden girdiğiniz API anahtarını kontrol edin. Detay: {lastException?.Message}",
            lastException);
    }

    private async Task<(string Content, string? FinishReason)> SendChatCompletionRequestAsync(
        string baseUrl,
        string keyToUse,
        string effectiveModel,
        bool isOpenRouter,
        bool isReasoningModel,
        object[] messages)
    {
        var requestPayload = new Dictionary<string, object>
        {
            ["model"] = effectiveModel,
            ["messages"] = messages,
            ["max_tokens"] = 16384
        };

        if (!isReasoningModel)
        {
            requestPayload["temperature"] = 0.2;
        }

        HttpResponseMessage? response = null;
        string responseString = string.Empty;

        for (int subTry = 0; subTry < 2; subTry++)
        {
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");
            var requestUrl = $"{baseUrl}/chat/completions";
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            requestMessage.Headers.Add("Authorization", $"Bearer {keyToUse}");
            if (isOpenRouter)
            {
                requestMessage.Headers.Add("HTTP-Referer", "https://videoozet.local");
                requestMessage.Headers.Add("X-Title", "VideoOzet");
            }
            requestMessage.Content = jsonContent;

            response = await _httpClient.SendAsync(requestMessage);
            responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                break;
            }

            if ((int)response.StatusCode == 400 && subTry == 0)
            {
                bool adjusted = false;
                if (responseString.Contains("temperature", StringComparison.OrdinalIgnoreCase) && requestPayload.ContainsKey("temperature"))
                {
                    _logger.LogInformation("Özetleme modeli {Model} temperature kabul etmedi. Parametre kaldırılarak deneniyor...", effectiveModel);
                    requestPayload.Remove("temperature");
                    adjusted = true;
                }
                if (responseString.Contains("max_tokens", StringComparison.OrdinalIgnoreCase) || responseString.Contains("tokens", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Özetleme modeli {Model} max_tokens sınırına takıldı. 8192 olarak küçültülüp deneniyor...", effectiveModel);
                    requestPayload["max_tokens"] = 8192;
                    adjusted = true;
                }
                if (adjusted) continue;
            }

            break;
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            if (responseString.Contains("429") || responseString.Contains("quota") || responseString.Contains("exhausted"))
            {
                throw new Exception($"Quota Exceeded: {responseString}");
            }
            if (responseString.Contains("503") || responseString.Contains("UNAVAILABLE") || responseString.Contains("high demand"))
            {
                throw new Exception($"High Demand: {responseString}");
            }

            throw new Exception($"API error: {(response != null ? (int)response.StatusCode : 0)} - {responseString}");
        }

        using var jsonDoc = JsonDocument.Parse(responseString);
        var root = jsonDoc.RootElement;

        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            string? finishReason = firstChoice.TryGetProperty("finish_reason", out var frProp) && frProp.ValueKind == JsonValueKind.String
                ? frProp.GetString()
                : null;

            if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
            {
                return (content.GetString() ?? string.Empty, finishReason);
            }
        }

        throw new Exception($"Unexpected API response format: {responseString}");
    }

    public static IReadOnlyList<string> SplitIntoSemanticSections(string text, int maxSectionChars = 20000)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        if (text.Length <= maxSectionChars)
            return new[] { text };

        var sections = new List<string>();
        int start = 0;
        int minSectionChars = Math.Max(2000, (int)(maxSectionChars * 0.60));

        while (start < text.Length)
        {
            int remaining = text.Length - start;
            if (remaining <= maxSectionChars)
            {
                var tail = text.Substring(start).Trim();
                if (!string.IsNullOrWhiteSpace(tail))
                    sections.Add(tail);
                break;
            }

            int maxEnd = start + maxSectionChars;
            int minEnd = start + minSectionChars;
            int splitAt = -1;

            // 1. Paragraf sınırı (\n\n)
            int paraIdx = text.LastIndexOf("\n\n", maxEnd - 1, maxEnd - minEnd, StringComparison.Ordinal);
            if (paraIdx >= minEnd)
            {
                splitAt = paraIdx + 2;
            }

            // 2. Satır sınırı (\n)
            if (splitAt < 0)
            {
                int lineIdx = text.LastIndexOf('\n', maxEnd - 1, maxEnd - minEnd);
                if (lineIdx >= minEnd)
                    splitAt = lineIdx + 1;
            }

            // 3. Cümle sınırı (. ! ?)
            if (splitAt < 0)
            {
                for (int idx = maxEnd - 1; idx >= minEnd; idx--)
                {
                    char c = text[idx];
                    if ((c == '.' || c == '!' || c == '?') && (idx + 1 >= text.Length || char.IsWhiteSpace(text[idx + 1])))
                    {
                        splitAt = idx + 1;
                        break;
                    }
                }
            }

            // 4. Kelime sınırı (boşluk)
            if (splitAt < 0)
            {
                int spaceIdx = text.LastIndexOf(' ', maxEnd - 1, maxEnd - minEnd);
                if (spaceIdx >= minEnd)
                    splitAt = spaceIdx + 1;
            }

            if (splitAt <= start)
                splitAt = maxEnd;

            var slice = text.Substring(start, splitAt - start).Trim();
            if (!string.IsNullOrWhiteSpace(slice))
            {
                sections.Add(slice);
            }

            start = splitAt;
        }

        return sections;
    }

    public static string DetectLanguageReliably(string? primaryText, string? secondaryText = null, string? candidateLang = null)
    {
        var normalizedCandidate = NormalizeLangCode(candidateLang);
        var sample = !string.IsNullOrWhiteSpace(primaryText) ? primaryText : (secondaryText ?? string.Empty);

        if (string.IsNullOrWhiteSpace(sample))
            return normalizedCandidate ?? "tr";

        if (sample.Length > 16000)
            sample = sample.Substring(0, 16000);

        int enMatches = EnglishStopWordsRegex.Matches(sample).Count;
        int trMatches = TurkishStopWordsRegex.Matches(sample).Count;
        int deMatches = GermanStopWordsRegex.Matches(sample).Count;

        int turkishChars = 0;
        foreach (char c in sample)
        {
            if (c is 'ç' or 'Ç' or 'ğ' or 'Ğ' or 'ı' or 'İ' or 'ö' or 'Ö' or 'ş' or 'Ş' or 'ü' or 'Ü')
                turkishChars++;
        }

        int trScore = trMatches * 2 + turkishChars;
        int enScore = enMatches * 2;
        int deScore = deMatches * 2;

        if (enMatches >= 5 && enScore > trScore * 2 && enScore >= deScore)
            return "en";

        if (deMatches >= 5 && deScore > trScore * 2 && deScore > enScore)
            return "de";

        if (trScore >= 6 && trScore > enScore)
            return "tr";

        if (!string.IsNullOrWhiteSpace(normalizedCandidate))
            return normalizedCandidate;

        if (enMatches > trMatches && enMatches >= 2)
            return "en";

        return "tr";
    }

    public static string? NormalizeLangCode(string? rawLang)
    {
        if (string.IsNullOrWhiteSpace(rawLang))
            return null;

        var l = rawLang.Trim().ToLowerInvariant();
        if (l.StartsWith("en") || l == "english" || l == "ingilizce") return "en";
        if (l.StartsWith("de") || l == "german" || l == "almanca" || l == "deutsch") return "de";
        if (l.StartsWith("fr") || l == "french" || l == "fransızca") return "fr";
        if (l.StartsWith("tr") || l == "turkish" || l == "türkçe" || l == "turkce") return "tr";
        return l.Length >= 2 ? l.Substring(0, 2) : l;
    }

    private static string EnsureValidSummaryJson(string rawResponse, string sourceTranscript)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return rawResponse;

        try
        {
            var parsed = LlmJson.Deserialize<InternalSummaryDto>(rawResponse);
            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.OzetMetni))
            {
                // Eğer metin yabancı dildeyse fakat JSON'da dil eksik ya da tr kalmışsa düzelt
                var verifiedLang = DetectLanguageReliably(sourceTranscript, parsed.OzetMetni, parsed.Dil);
                if (!string.Equals(NormalizeLangCode(parsed.Dil) ?? "tr", verifiedLang, StringComparison.OrdinalIgnoreCase))
                {
                    return JsonSerializer.Serialize(new
                    {
                        dil = verifiedLang,
                        ozetMetni = parsed.OzetMetni,
                        konuBasliklari = parsed.KonuBasliklari ?? Array.Empty<string>(),
                        konuEtiketleri = parsed.KonuEtiketleri ?? Array.Empty<string>()
                    }, UnescapedJsonOptions);
                }

                return rawResponse;
            }
        }
        catch
        {
            // JSON yarım kalmış olabilir, kurtarma adımına geç
        }

        var recovered = TryParseOrRecoverSummary(rawResponse, sourceTranscript);
        return JsonSerializer.Serialize(new
        {
            dil = recovered.Dil ?? DetectLanguageReliably(sourceTranscript, recovered.OzetMetni, null),
            ozetMetni = recovered.OzetMetni,
            konuBasliklari = recovered.KonuBasliklari ?? Array.Empty<string>(),
            konuEtiketleri = recovered.KonuEtiketleri ?? Array.Empty<string>()
        }, UnescapedJsonOptions);
    }

    public static (string? Dil, string OzetMetni, string[]? KonuBasliklari, string[]? KonuEtiketleri) TryParseOrRecoverSummary(
        string rawResponse,
        string? sourceTranscript = null)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            var fallbackLang = DetectLanguageReliably(sourceTranscript, null, null);
            return (fallbackLang, string.Empty, null, null);
        }

        try
        {
            var parsed = LlmJson.Deserialize<InternalSummaryDto>(rawResponse);
            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.OzetMetni))
            {
                var lang = DetectLanguageReliably(sourceTranscript, parsed.OzetMetni, parsed.Dil);
                return (lang, parsed.OzetMetni, parsed.KonuBasliklari, parsed.KonuEtiketleri);
            }
        }
        catch
        {
            // Aşağıdaki regex kurtarma mantığı devreye girer
        }

        string? extractedDil = null;
        var dilMatch = Regex.Match(rawResponse, @"""dil""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if (dilMatch.Success)
        {
            extractedDil = NormalizeLangCode(dilMatch.Groups[1].Value);
        }

        string extractedOzet = rawResponse.Trim();
        var ozetKeyIdx = rawResponse.IndexOf(@"""ozetMetni""", StringComparison.OrdinalIgnoreCase);
        if (ozetKeyIdx >= 0)
        {
            int colonIdx = rawResponse.IndexOf(':', ozetKeyIdx + 11);
            if (colonIdx > 0)
            {
                int firstQuote = rawResponse.IndexOf('"', colonIdx + 1);
                if (firstQuote > 0)
                {
                    var sb = new StringBuilder();
                    bool escaped = false;
                    for (int i = firstQuote + 1; i < rawResponse.Length; i++)
                    {
                        char c = rawResponse[i];
                        if (escaped)
                        {
                            sb.Append(c switch
                            {
                                'n' => '\n',
                                'r' => '\r',
                                't' => '\t',
                                '"' => '"',
                                '\\' => '\\',
                                _ => c
                            });
                            escaped = false;
                        }
                        else if (c == '\\')
                        {
                            escaped = true;
                        }
                        else if (c == '"')
                        {
                            break;
                        }
                        else
                        {
                            sb.Append(c);
                        }
                    }

                    var candidateOzet = sb.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(candidateOzet))
                    {
                        extractedOzet = candidateOzet;
                    }
                }
            }
        }

        var finalLang = DetectLanguageReliably(sourceTranscript, extractedOzet, extractedDil);
        return (finalLang, extractedOzet, null, null);
    }

    private sealed class InternalSummaryDto
    {
        public string? Dil { get; set; }
        public string OzetMetni { get; set; } = string.Empty;
        public string[]? KonuBasliklari { get; set; }
        public string[]? KonuEtiketleri { get; set; }
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

        // Baş (%35) + orta-1 (%25) + orta-2 (%20) + son (%20) dağıtık pencereleme
        int headLen = (int)(maxChars * 0.35);
        int mid1Len = (int)(maxChars * 0.25);
        int mid2Len = (int)(maxChars * 0.20);
        int tailLen = Math.Max(400, maxChars - headLen - mid1Len - mid2Len);

        int mid1Start = Math.Max(headLen, (int)(prompt.Length * 0.35));
        int mid2Start = Math.Max(mid1Start + mid1Len, (int)(prompt.Length * 0.65));
        int tailStart = Math.Max(mid2Start + mid2Len, prompt.Length - tailLen);

        var sb = new StringBuilder(maxChars + 128);
        sb.Append(prompt.AsSpan(0, headLen));
        if (mid1Start + mid1Len < tailStart)
        {
            sb.Append("\n\n[... ORTA BÖLÜM 1 ...]\n\n");
            sb.Append(prompt.AsSpan(mid1Start, Math.Min(mid1Len, prompt.Length - mid1Start)));
        }
        if (mid2Start + mid2Len < tailStart)
        {
            sb.Append("\n\n[... ORTA BÖLÜM 2 ...]\n\n");
            sb.Append(prompt.AsSpan(mid2Start, Math.Min(mid2Len, prompt.Length - mid2Start)));
        }
        sb.Append("\n\n[... SON BÖLÜM ...]\n\n");
        sb.Append(prompt.AsSpan(tailStart));
        return sb.ToString();
    }
}
