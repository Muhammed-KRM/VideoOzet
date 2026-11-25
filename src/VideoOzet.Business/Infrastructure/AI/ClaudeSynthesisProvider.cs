using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class ClaudeSynthesisProvider : ISynthesisProvider
{
    private readonly ILogger<ClaudeSynthesisProvider> _logger;
    private readonly IConfiguration _configuration;
    private readonly IAiProviderSettingsService? _settingsService;

    public ClaudeSynthesisProvider(
        ILogger<ClaudeSynthesisProvider> logger,
        IConfiguration configuration)
        : this(logger, configuration, null)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public ClaudeSynthesisProvider(
        ILogger<ClaudeSynthesisProvider> logger,
        IConfiguration configuration,
        IAiProviderSettingsService? settingsService)
    {
        _logger = logger;
        _configuration = configuration;
        _settingsService = settingsService;
    }

    /// <summary>
    /// CallLlmAsync ile aynı sağlayıcı seçim sırasını kullanır (Aktif Ayarlar → Anthropic → Gemini → OpenAI).
    /// </summary>
    public string ActiveModelName
    {
        get
        {
            if (_settingsService != null)
            {
                try
                {
                    var settings = _settingsService.GetSettingsAsync().GetAwaiter().GetResult();
                    return settings.GetEffectiveVideoPlanModel();
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(_configuration["ANTHROPIC_API_KEY"]))
                return _configuration["CLAUDE_MODEL"] ?? "claude-3-5-sonnet-20240620";

            if (!string.IsNullOrWhiteSpace(_configuration["GEMINI_API_KEY"]))
                return _configuration["GEMINI_MODEL"] ?? "gemini-3.8-flash-tiered";

            if (!string.IsNullOrWhiteSpace(_configuration["OPENAI_API_KEY"] ?? _configuration["OpenAI:ApiKey"]))
                return _configuration["OPENAI_MODEL"] ?? "gpt-4o";

            return "mock";
        }
    }

    private async Task<string> CallLlmAsync(string prompt, CancellationToken ct, string taskType = "videoplan")
    {
        var (text, finishReason) = await CallLlmRawAsync(prompt, ct, taskType);
        text = StripReasoningTags(text);

        if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finishReason, "max_tokens", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("LLM yanıtı token sınırına çarptı (finish_reason={FinishReason}, len={Len}). Kaldığı yerden devam ettirme (continuation) çağrısı yapılıyor...", finishReason, text.Length);
            try
            {
                var trimmedStart = text.TrimStart();
                bool isJsonOutput = trimmedStart.StartsWith("{") || trimmedStart.StartsWith("[");

                if (isJsonOutput)
                {
                    string headSnippet = trimmedStart.Length > 400 ? trimmedStart.Substring(0, 400) : trimmedStart;
                    string tailSnippet = text.Length > 1200 ? text.Substring(text.Length - 1200) : text;
                    string continuationPrompt = $"""
                        Aşağıdaki JSON çıktısı token sınırı nedeniyle tam son karakterde kesildi.

                        --- JSON BAŞLANGICI (ŞEMA HATIRLATMASI) ---
                        {headSnippet}
                        --- YARIM KALAN SON KISIM ---
                        {tailSnippet}
                        --- KESİLEN NOKTA ---

                        GÖREV: Yukarıdaki JSON metninin TAM KESİLDİĞİ KARAKTERDEN itibaren hiçbir şeyi tekrarlamadan, Markdown açıklaması yazmadan ve şema dışı uydurma alan eklemeden doğrudan devam et; kalan elemanları ve tüm açık tırnak/dizi/nesne parantezlerini kapatarak geçerli JSON olarak bitir.
                        """;
                    var (contText, _) = await CallLlmRawAsync(continuationPrompt, ct, taskType);
                    contText = StripReasoningTags(contText);
                    if (!string.IsNullOrWhiteSpace(contText))
                    {
                        var cleanCont = Regex.Replace(contText.Trim(), @"^```(?:json)?\s*|\s*```$", string.Empty, RegexOptions.IgnoreCase);
                        text = text + cleanCont;
                    }
                }
                else if (text.Contains("## Sahne", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("### Sahne", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("Prompter Metni", StringComparison.OrdinalIgnoreCase))
                {
                    string tailSnippet = text.Length > 1600 ? text.Substring(text.Length - 1600) : text;
                    string continuationPrompt = $"""
                        Ürettiğin Video Senaryo/Prompter metni token sınırı nedeniyle aşağıdaki satırın sonunda yarım kaldı:

                        --- YARIM KALAN SON KISIM ---
                        {tailSnippet}
                        --- KESİLEN NOKTA ---

                        KESİN KURALLAR:
                        1. Yukarıdaki yarım kalan cümlenin TAM KALDIĞI KELİMEDEN devam et, hiçbir başlığı veya paragrafı tekrarlama.
                        2. Bu bir Video Sahne Planıdır (`## Sahne ...`, `### 1. Görsel & Edit Yönergesi`, `### 2. Prompter Metni (Aynen Okunacak)` ve `>` prompter satırları). ASLA `## Sonuç`, `## Değerlendirme` veya numaralı düz makale başlığı (`## 4. ...`) YAZMA!
                        3. Yalnızca yarım kalan sahnenin `>` prompter paragraflarını ve varsa bu grupta eksik kalan `## Sahne` bloğunu aynı stüdyo formatıyla tamamlayıp dur.
                        """;
                    var (contText, _) = await CallLlmRawAsync(continuationPrompt, ct, taskType);
                    contText = StripReasoningTags(contText);
                    if (!string.IsNullOrWhiteSpace(contText))
                    {
                        var trimmedBase = text.TrimEnd();
                        int lastNl = trimmedBase.LastIndexOf('\n');
                        var lastLine = lastNl >= 0 ? trimmedBase.Substring(lastNl + 1).TrimStart() : trimmedBase.TrimStart();
                        bool midQuoteSentence = lastLine.StartsWith(">") &&
                                                !lastLine.EndsWith(".") &&
                                                !lastLine.EndsWith("!") &&
                                                !lastLine.EndsWith("?") &&
                                                !lastLine.EndsWith("\"") &&
                                                !lastLine.EndsWith("”");

                        if (midQuoteSentence)
                        {
                            var cleanNext = contText.TrimStart().TrimStart('>').TrimStart();
                            text = trimmedBase + (cleanNext.StartsWith(",") || cleanNext.StartsWith(";") || cleanNext.StartsWith(".") ? "" : " ") + cleanNext;
                        }
                        else
                        {
                            text = trimmedBase + "\n" + contText.TrimStart();
                        }
                    }
                }
                else
                {
                    string tailSnippet = text.Length > 500 ? text.Substring(text.Length - 500) : text;
                    string continuationPrompt = $"""
                        Ürettiğin doküman çıktı token sınırı nedeniyle aşağıdaki metnin sonunda yarım kaldı:

                        --- YARIM KALAN SON KISIM ---
                        {tailSnippet}
                        --- SON ---

                        GÖREV: Yukarıdaki son cümlenin kaldığı yerden HİÇBİR BAŞLIĞI VEYA PARAGRAFI TEKRARLAMADAN doğrudan devam et ve dokümanı Sonuç / Kapanış bölümüne kadar aynı Markdown formatıyla eksiksiz tamamla.
                        """;
                    var (contText, _) = await CallLlmRawAsync(continuationPrompt, ct, taskType);
                    contText = StripReasoningTags(contText);
                    if (!string.IsNullOrWhiteSpace(contText))
                    {
                        text = text.TrimEnd() + "\n" + contText.TrimStart();
                    }
                }
            }
            catch (Exception contEx) when (contEx is not OperationCanceledException)
            {
                _logger.LogWarning(contEx, "Continuation çağrısı başarısız oldu, mevcut metin korunuyor.");
            }
        }

        return text;
    }

    public static string NormalizeMarkdownOutput(string text, bool isVideoPlan = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = StripReasoningTags(text)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        // Eğer JSON çıktısıysa dokunma
        if (cleaned.StartsWith("{") || cleaned.StartsWith("["))
            return cleaned;

        // Başlık veya içindekiler listesindeki köşeli parantez kalıntılarını temizle: "## 2. [Başlık]" -> "## 2. Başlık", "- 2. [Başlık]" -> "- 2. Başlık"
        cleaned = Regex.Replace(
            cleaned,
            @"(?m)^(\s*(?:#{1,4}|[-*])\s*(?:\d+(?:\.\d+)*\.?\s*)?)\[([^\]\r\n]+)\]\s*$",
            "$1$2");

        // Sahne başlığındaki köşeli parantezleri temizle: "## Sahne 1: [Başlık]" -> "## Sahne 1: Başlık"
        cleaned = Regex.Replace(
            cleaned,
            @"(?m)^(\s*#{1,4}\s*Sahne\s*\d+\s*[:–-]\s*)\[([^\]\r\n]+)\]",
            "$1$2");

        // Hedef süre normalize
        cleaned = Regex.Replace(
            cleaned,
            @"(?mi)^(\*\*Hedef Süre:\*\*\s*)uzun\s*$",
            "$1~15-20 Dakika (Uzun ve Detaylı)");
        cleaned = Regex.Replace(
            cleaned,
            @"(?mi)^(\*\*Hedef Süre:\*\*\s*)orta\s*$",
            "$1~10-12 Dakika (Orta)");
        cleaned = Regex.Replace(
            cleaned,
            @"(?mi)^(\*\*Hedef Süre:\*\*\s*)k[ıi]sa\s*$",
            "$1~5-8 Dakika (Kısa)");

        // Prompter blockquote paragrafları arasına boş '>' satırı ekle
        cleaned = Regex.Replace(
            cleaned,
            @"(?m)^(>\s*\S.*)\n(?=>\s*[""“”])",
            "$1\n>\n");

        if (isVideoPlan)
        {
            // Yanlışlıkla satır ortasında kırılmış '>' prompter cümlelerini birleştir
            cleaned = Regex.Replace(
                cleaned,
                @"(?m)^(>\s*[^\n]*[^\.\!\?""”\s])\n\s*([,;:a-zçğıöşü][^\n]+)$",
                "$1 $2");

            // Sahne planının sonuna sızmış olabilecek düz makale başlıklarını ("## 4. ...", "## Sonuç") temizle
            if (cleaned.Contains("## Sahne", StringComparison.OrdinalIgnoreCase) ||
                cleaned.Contains("### Sahne", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = Regex.Replace(
                    cleaned,
                    @"(?ms)\n(?:---\s*\n+)?##\s+(?:Sonuç\b|\d+\.\s+(?!Sahne\b)).*$",
                    string.Empty);
            }

            int idxPlan = cleaned.IndexOf("# Video Planı", StringComparison.OrdinalIgnoreCase);
            if (idxPlan < 0) idxPlan = cleaned.IndexOf("## Sahne 1", StringComparison.OrdinalIgnoreCase);
            if (idxPlan > 0 && cleaned.Substring(0, idxPlan).Contains("Araştırma Özeti", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(idxPlan).TrimStart();
            }
        }
        else
        {
            int idxVp = cleaned.IndexOf("# Video Planı", StringComparison.OrdinalIgnoreCase);
            if (idxVp < 0) idxVp = cleaned.IndexOf("## Video Planı", StringComparison.OrdinalIgnoreCase);
            if (idxVp > 0)
            {
                cleaned = cleaned.Substring(0, idxVp).TrimEnd('-', '#', ' ', '\r', '\n');
            }
        }

        return cleaned.Trim();
    }

    private static string StripReasoningTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = Regex.Replace(
            text,
            @"<(think|thought|thinking)>.*?(</\1>|$)",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();

        return cleaned;
    }

    private static string NormalizeTargetLength(string targetLength)
    {
        if (string.IsNullOrWhiteSpace(targetLength))
        {
            var defaultBudget = PrompterPacingCalculator.CalculateBudget(10);
            int defaultScenes = PrompterPacingCalculator.CalculateTargetSceneCount(10);
            return $"~10 Dakika ({defaultScenes} sahne, KESİN ARALIK: {defaultBudget.MinWords}–{defaultBudget.MaxWords} kelime, ideal ~{defaultBudget.TargetWords} kelime prompter konuşma metni — dakika başı ~{PrompterPacingCalculator.TargetWordsPerMinute} kelime Türkçe stüdyo temposu; {defaultBudget.MaxWords} kelimeyi AŞMA!)";
        }

        int? parsedMinutes = PrompterPacingCalculator.TryParseTargetMinutes(targetLength);
        if (parsedMinutes.HasValue)
        {
            int mins = parsedMinutes.Value;
            var budget = PrompterPacingCalculator.CalculateBudget(mins);
            int scenes = PrompterPacingCalculator.CalculateTargetSceneCount(mins);

            var normalized = targetLength.Trim().ToLowerInvariant();
            if (normalized is "kisa" or "kısa" || (normalized.Contains("kısa") && !normalized.Contains("dk") && !normalized.Contains("dakika")))
                return $"~5-7 Dakika (Kısa — {scenes} sahne, KESİN ARALIK: {budget.MinWords}–{budget.MaxWords} kelime, ideal ~{budget.TargetWords} kelime prompter konuşma metni — ~{PrompterPacingCalculator.TargetWordsPerMinute} kelime/dk; {budget.MaxWords} kelimeyi AŞMA!)";
            if (normalized is "orta" || (normalized.Contains("orta") && !normalized.Contains("dk") && !normalized.Contains("dakika")))
                return $"~10 Dakika (Orta — {scenes} sahne, KESİN ARALIK: {budget.MinWords}–{budget.MaxWords} kelime, ideal ~{budget.TargetWords} kelime prompter konuşma metni — ~{PrompterPacingCalculator.TargetWordsPerMinute} kelime/dk; {budget.MaxWords} kelimeyi AŞMA!)";
            if (normalized is "uzun" || (normalized.Contains("uzun") && !normalized.Contains("dk") && !normalized.Contains("dakika")))
                return $"~15-18 Dakika (Uzun — {scenes} sahne, KESİN ARALIK: {budget.MinWords}–{budget.MaxWords} kelime, ideal ~{budget.TargetWords} kelime prompter konuşma metni — ~{PrompterPacingCalculator.TargetWordsPerMinute} kelime/dk; {budget.MaxWords} kelimeyi AŞMA!)";

            return $"{mins} Dakika ({scenes} sahne, KESİN ARALIK: {budget.MinWords}–{budget.MaxWords} kelime, ideal ~{budget.TargetWords} kelime prompter konuşma metni — dakika başı ~{PrompterPacingCalculator.TargetWordsPerMinute} kelime Türkçe stüdyo temposu; {budget.MaxWords} kelimeyi AŞMA!)";
        }

        return targetLength;
    }

    private static bool IsReasoningModelName(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return false;
        return modelName.Contains("r1", StringComparison.OrdinalIgnoreCase) ||
               modelName.Contains("o1", StringComparison.OrdinalIgnoreCase) ||
               modelName.Contains("o3", StringComparison.OrdinalIgnoreCase) ||
               modelName.Contains("argon", StringComparison.OrdinalIgnoreCase) ||
               modelName.Contains("reasoning", StringComparison.OrdinalIgnoreCase) ||
               modelName.Contains("thinking", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(string Text, string FinishReason)> CallLlmRawAsync(string prompt, CancellationToken ct, string taskType = "videoplan")
    {
        AiProviderSettingsDto? settings = null;
        if (_settingsService != null)
        {
            try
            {
                settings = await _settingsService.GetSettingsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI Provider ayarları okunamadı, varsayılan yapılandırmaya dönülüyor.");
            }
        }

        string GetTargetModel(AiProviderSettingsDto s) => taskType switch
        {
            "qc" => s.GetEffectiveQcModel(),
            "summary" => s.GetEffectiveSummaryModel(),
            _ => s.GetEffectiveVideoPlanModel()
        };

        bool allowFallback = settings == null || settings.AllowFallbackToSecondaryProvider;

        // 1. OpenRouter seçiliyse OpenRouter API'ye gönder (Kesin Sağlayıcı Kilidi + Exponential Backoff)
        if (settings != null && settings.ActiveProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            var openRouterModel = GetTargetModel(settings);
            if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
            {
                if (!allowFallback)
                {
                    throw new InvalidOperationException(
                        $"Seçili sağlayıcı 'OpenRouter' (Model: '{openRouterModel}') için API anahtarı tanımlanmamış. " +
                        "Kesin Sağlayıcı Kilidi (AllowFallbackToSecondaryProvider=false) aktif olduğu için alt modele düşülmedi.");
                }
            }
            else
            {
                var openRouterUrl = $"{settings.OpenRouterBaseUrl.TrimEnd('/')}/chat/completions";
                var orKeys = settings.OpenRouterApiKey
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .ToArray();

                Exception? lastOrEx = null;
                int maxOrRetries = Math.Max(orKeys.Length, 3);

                for (int tryCount = 0; tryCount < maxOrRetries; tryCount++)
                {
                    var keyToUse = orKeys[tryCount % orKeys.Length];
                    try
                    {
                        using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(20) };

                        var requestPayload = new Dictionary<string, object>
                        {
                            ["model"] = openRouterModel,
                            ["messages"] = new[] { new { role = "user", content = prompt } },
                            ["max_tokens"] = 16384
                        };

                        if (!IsReasoningModelName(openRouterModel))
                        {
                            requestPayload["temperature"] = 0.2;
                        }

                        System.Net.Http.HttpResponseMessage? response = null;
                        string responseString = string.Empty;

                        for (int subTry = 0; subTry < 2; subTry++)
                        {
                            var jsonContent = new System.Net.Http.StringContent(
                                JsonSerializer.Serialize(requestPayload),
                                Encoding.UTF8,
                                "application/json");

                            using var requestMessage = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, openRouterUrl);
                            requestMessage.Headers.Add("Authorization", $"Bearer {keyToUse}");
                            requestMessage.Headers.Add("HTTP-Referer", "https://videoozet.local");
                            requestMessage.Headers.Add("X-Title", "VideoOzet");
                            requestMessage.Content = jsonContent;

                            response = await httpClient.SendAsync(requestMessage, ct);
                            responseString = await response.Content.ReadAsStringAsync(ct);

                            if (response.IsSuccessStatusCode)
                            {
                                break;
                            }

                            if ((int)response.StatusCode == 400 && subTry == 0)
                            {
                                bool adjusted = false;
                                if (responseString.Contains("temperature", StringComparison.OrdinalIgnoreCase) && requestPayload.ContainsKey("temperature"))
                                {
                                    _logger.LogInformation("OpenRouter modeli {Model} temperature parametresini kabul etmedi, kaldırılarak deneniyor...", openRouterModel);
                                    requestPayload.Remove("temperature");
                                    adjusted = true;
                                }
                                if (responseString.Contains("max_tokens", StringComparison.OrdinalIgnoreCase) || responseString.Contains("tokens", StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger.LogInformation("OpenRouter modeli {Model} max_tokens sınırına takıldı, 8192 olarak deneniyor...", openRouterModel);
                                    requestPayload["max_tokens"] = 8192;
                                    adjusted = true;
                                }
                                if (adjusted) continue;
                            }

                            break;
                        }

                        if (response != null && response.IsSuccessStatusCode)
                        {
                            using var jsonDoc = JsonDocument.Parse(responseString);
                            var root = jsonDoc.RootElement;
                            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                            {
                                var firstChoice = choices[0];
                                var finishReason = firstChoice.TryGetProperty("finish_reason", out var fr) ? (fr.GetString() ?? "stop") : "stop";
                                if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                                {
                                    var textVal = content.GetString();
                                    if (!string.IsNullOrWhiteSpace(textVal))
                                        return (textVal, finishReason);
                                }
                            }
                            throw new InvalidOperationException($"OpenRouter ({openRouterModel}) boş yanıt döndürdü: {responseString}");
                        }

                        throw new InvalidOperationException($"OpenRouter API HTTP {(response != null ? (int)response.StatusCode : 0)} hatası: {responseString}");
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastOrEx = ex;
                        int backoffSeconds = (int)Math.Min(16, Math.Pow(2, tryCount + 1)); // 2s, 4s, 8s
                        _logger.LogWarning(
                            "OpenRouter ({Model}) deneme {Try}/{Max} başarısız oldu: {Error}",
                            openRouterModel, tryCount + 1, maxOrRetries, ex.Message);

                        if (tryCount < maxOrRetries - 1)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);
                        }
                    }
                }

                if (!allowFallback)
                {
                    throw new InvalidOperationException(
                        $"Seçili sağlayıcı 'OpenRouter' ve model '{openRouterModel}' {maxOrRetries} deneme sonunda yanıt veremedi. " +
                        $"Kesin Sağlayıcı Kilidi aktif olduğu için Groq veya başka bir alt modele düşülmedi. Son hata: {lastOrEx?.Message}",
                        lastOrEx);
                }

                _logger.LogWarning("OpenRouter ({Model}) tüm denemelerde başarısız oldu; AllowFallbackToSecondaryProvider=true olduğu için yedek sağlayıcıya geçiliyor.", openRouterModel);
            }
        }

        // 2. Yerel Antigravity Proxy seçiliyse (veya settings == null olup GEMINI_API_KEY tanımlıysa)
        bool isExplicitAntigravity = settings != null && settings.ActiveProvider.Equals("antigravity", StringComparison.OrdinalIgnoreCase);
        bool isLegacyGeminiProxy = settings == null && !string.IsNullOrWhiteSpace(_configuration["GEMINI_API_KEY"]) && string.IsNullOrWhiteSpace(_configuration["ANTHROPIC_API_KEY"]);

        if (isExplicitAntigravity || isLegacyGeminiProxy)
        {
            var proxyModel = settings != null
                ? GetTargetModel(settings)
                : (_configuration["GEMINI_MODEL"] ?? "gemini-3.8-flash-tiered");
            var proxyBaseUrl = settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyUrl)
                ? settings.AntigravityProxyUrl.TrimEnd('/')
                : "http://localhost:8045/v1";
            var rawProxyKey = settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyKey)
                ? settings.AntigravityProxyKey
                : (_configuration["GEMINI_API_KEY"] ?? string.Empty);

            var apiKeys = string.IsNullOrWhiteSpace(rawProxyKey)
                ? new[] { string.Empty }
                : rawProxyKey.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .ToArray();

            Exception? lastProxyEx = null;
            int maxRetries = Math.Max(apiKeys.Length, 3);

            for (int tryCount = 0; tryCount < maxRetries; tryCount++)
            {
                var keyToUse = apiKeys[tryCount % apiKeys.Length];

                try
                {
                    using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(6) };
                    int initialMaxTokens = proxyModel.Contains("gemini", StringComparison.OrdinalIgnoreCase) ? 65536 : 16384;

                    var requestPayload = new Dictionary<string, object>
                    {
                        ["model"] = proxyModel,
                        ["messages"] = new[]
                        {
                            new { role = "user", content = prompt }
                        },
                        ["max_tokens"] = initialMaxTokens
                    };

                    if (!IsReasoningModelName(proxyModel))
                    {
                        requestPayload["temperature"] = 0.2;
                    }

                    var requestUrl = $"{proxyBaseUrl}/chat/completions";
                    System.Net.Http.HttpResponseMessage? response = null;
                    string responseString = string.Empty;

                    for (int subTry = 0; subTry < 3; subTry++)
                    {
                        var jsonContent = new System.Net.Http.StringContent(
                            JsonSerializer.Serialize(requestPayload),
                            Encoding.UTF8,
                            "application/json");
                        using var requestMessage = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, requestUrl);
                        if (!string.IsNullOrWhiteSpace(keyToUse))
                        {
                            requestMessage.Headers.Add("Authorization", $"Bearer {keyToUse}");
                        }
                        requestMessage.Content = jsonContent;

                        response = await httpClient.SendAsync(requestMessage, ct);
                        responseString = await response.Content.ReadAsStringAsync(ct);

                        if (response.IsSuccessStatusCode)
                        {
                            break;
                        }

                        if ((int)response.StatusCode == 400 && subTry < 2)
                        {
                            bool adjusted = false;
                            if (responseString.Contains("temperature", StringComparison.OrdinalIgnoreCase) && requestPayload.ContainsKey("temperature"))
                            {
                                _logger.LogInformation("Proxy modeli {Model} temperature kabul etmedi. Parametre kaldırılarak deneniyor...", proxyModel);
                                requestPayload.Remove("temperature");
                                adjusted = true;
                            }
                            if (responseString.Contains("max_tokens", StringComparison.OrdinalIgnoreCase) || responseString.Contains("tokens", StringComparison.OrdinalIgnoreCase))
                            {
                                int currentMax = requestPayload["max_tokens"] is int cm ? cm : 16384;
                                int nextMax = currentMax > 32768 ? 32768 : (currentMax > 16384 ? 16384 : 8192);
                                _logger.LogInformation("Proxy modeli {Model} max_tokens={Cur} sınırına takıldı. {Next} olarak deneniyor...", proxyModel, currentMax, nextMax);
                                requestPayload["max_tokens"] = nextMax;
                                adjusted = true;
                            }
                            if (adjusted) continue;
                        }

                        break;
                    }

                    if (response != null && response.IsSuccessStatusCode)
                    {
                        using var jsonDoc = JsonDocument.Parse(responseString);
                        var root = jsonDoc.RootElement;
                        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var firstChoice = choices[0];
                            var finishReason = firstChoice.TryGetProperty("finish_reason", out var fr) ? (fr.GetString() ?? "stop") : "stop";
                            if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                            {
                                var textResult = content.GetString();
                                if (!string.IsNullOrWhiteSpace(textResult))
                                    return (textResult, finishReason);
                            }
                        }
                        throw new InvalidOperationException($"Yerel Proxy ({proxyModel}) boş içerik döndürdü: {responseString}");
                    }

                    throw new InvalidOperationException($"Yerel Proxy ({proxyBaseUrl}) HTTP {(response != null ? (int)response.StatusCode : 0)} hatası: {responseString}");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastProxyEx = ex;
                    int backoffSeconds = (int)Math.Min(16, Math.Pow(2, tryCount + 1)); // 2s, 4s, 8s
                    _logger.LogWarning(
                        "Antigravity Yerel Proxy ({Model}) deneme {Try}/{Max} başarısız oldu: {Message}",
                        proxyModel, tryCount + 1, maxRetries, ex.Message);

                    if (tryCount < maxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);
                    }
                }
            }

            if (isExplicitAntigravity && !allowFallback)
            {
                throw new InvalidOperationException(
                    $"Seçili sağlayıcı 'Antigravity Yerel Proxy ({proxyBaseUrl})' ve model '{proxyModel}' {maxRetries} deneme sonunda yanıt veremedi. " +
                    $"Kesin Sağlayıcı Kilidi aktif olduğu için Groq sıkıştırılmış moduna düşülmedi. Son hata: {lastProxyEx?.Message}",
                    lastProxyEx);
            }

            if (isExplicitAntigravity)
            {
                _logger.LogWarning("Antigravity Proxy ({Model}) başarısız oldu; AllowFallbackToSecondaryProvider=true olduğu için yedek sağlayıcıya geçiliyor.", proxyModel);
            }
        }

        // 3. Doğrudan Anthropic (yalnızca settings yokken veya fallback izni açıkken)
        var anthropicKey = _configuration["ANTHROPIC_API_KEY"];
        if (!string.IsNullOrWhiteSpace(anthropicKey) && (settings == null || allowFallback))
        {
            var model = _configuration["CLAUDE_MODEL"] ?? "claude-3-5-sonnet-20240620";
            var client = new AnthropicClient(anthropicKey);
            var messages = new List<Anthropic.SDK.Messaging.Message>
            {
                new Anthropic.SDK.Messaging.Message(RoleType.User, prompt)
            };

            var parameters = new MessageParameters
            {
                Messages = messages,
                MaxTokens = 8192,
                Model = model,
                Temperature = 0.2m
            };

            var response = await client.Messages.GetClaudeMessageAsync(parameters, ct);
            return ((response.Content[0] as TextContent)?.Text ?? string.Empty, response.StopReason ?? "stop");
        }

        // 4. Groq Cloud LLM: YALNIZCA ActiveProvider == "groq" ise VEYA AllowFallbackToSecondaryProvider == true (veya settings == null) ise çalışır!
        bool isExplicitGroq = settings != null && settings.ActiveProvider.Equals("groq", StringComparison.OrdinalIgnoreCase);
        if (isExplicitGroq || allowFallback)
        {
            var groqKey = settings != null && !string.IsNullOrWhiteSpace(settings.GroqApiKey)
                ? settings.GroqApiKey
                : (_configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"]);

            if (isExplicitGroq && string.IsNullOrWhiteSpace(groqKey) && !allowFallback)
            {
                throw new InvalidOperationException(
                    $"Seçili sağlayıcı 'Groq' (Model: '{GetTargetModel(settings!)}') için API anahtarı tanımlanmamış.");
            }

            if (!string.IsNullOrWhiteSpace(groqKey))
            {
                var preferredGroqModel = settings != null ? GetTargetModel(settings) : "openai/gpt-oss-120b";
                var candidateModels = isExplicitGroq && !allowFallback
                    ? new[] { preferredGroqModel }
                    : new[] { preferredGroqModel, "openai/gpt-oss-20b", "qwen/qwen3.8-27b", "openai/gpt-oss-120b" }
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                string lastGroqError = string.Empty;
                int[] maxCharsPasses = { 9500, 7200, 5800 };
                for (int pass = 0; pass < maxCharsPasses.Length; pass++)
                {
                    if (pass > 0)
                    {
                        int waitSeconds = pass == 1 ? 6 : 12;
                        _logger.LogInformation("Groq yeniden deneme öncesi {Wait} saniye bekleniyor (Pass {Pass})...", waitSeconds, pass + 1);
                        await Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct);
                    }

                    var groqPrompt = CompactPromptForGroq(prompt, maxCharsPasses[pass]);
                    foreach (var groqModel in candidateModels)
                    {
                        try
                        {
                            _logger.LogInformation("Groq Cloud LLM ({Model}) ile sentez çağrısı yapılıyor (Prompt: {Len} krk)...", groqModel, groqPrompt.Length);
                            using var groqClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                            var groqBody = new
                            {
                                model = groqModel,
                                messages = new[] { new { role = "user", content = groqPrompt } },
                                max_tokens = 8192,
                                temperature = 0.2
                            };
                            using var groqReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                            groqReq.Headers.Add("Authorization", $"Bearer {groqKey.Trim()}");
                            groqReq.Headers.Add("User-Agent", "VideoOzet/1.0");
                            groqReq.Content = new System.Net.Http.StringContent(
                                JsonSerializer.Serialize(groqBody),
                                Encoding.UTF8,
                                "application/json");

                            var groqResp = await groqClient.SendAsync(groqReq, ct);
                            var groqStr = await groqResp.Content.ReadAsStringAsync(ct);
                            if (groqResp.IsSuccessStatusCode)
                            {
                                using var doc = JsonDocument.Parse(groqStr);
                                if (doc.RootElement.TryGetProperty("choices", out var ch) && ch.GetArrayLength() > 0 &&
                                    ch[0].TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var cnt))
                                {
                                    var finishReason = ch[0].TryGetProperty("finish_reason", out var fr) ? (fr.GetString() ?? "stop") : "stop";
                                    var textResult = cnt.GetString();
                                    if (!string.IsNullOrWhiteSpace(textResult))
                                        return (textResult, finishReason);
                                }
                            }
                            else
                            {
                                lastGroqError = $"HTTP {(int)groqResp.StatusCode}: {groqStr}";
                                _logger.LogWarning("Groq LLM ({Model}) returned {Status}: {Body}", groqModel, groqResp.StatusCode, groqStr);
                            }
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception gEx)
                        {
                            lastGroqError = gEx.Message;
                            _logger.LogWarning(gEx, "Groq LLM ({Model}) sentez çağrısı başarısız oldu.", groqModel);
                        }
                    }
                }

                if (isExplicitGroq && !allowFallback)
                {
                    throw new InvalidOperationException(
                        $"Seçili sağlayıcı 'Groq' ve model '{preferredGroqModel}' tüm denemelerde başarısız oldu. Son hata: {lastGroqError}");
                }
            }
        }

        var openAiKey = _configuration["OPENAI_API_KEY"] ?? _configuration["OpenAI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(openAiKey) && (settings == null || allowFallback))
        {
            var openAiModel = _configuration["OPENAI_MODEL"] ?? "gpt-4o";
            var chatClient = new ChatClient(openAiModel, openAiKey);
            var chatMessages = new List<ChatMessage> { new UserChatMessage(prompt) };
            var chatResponse = await chatClient.CompleteChatAsync(chatMessages, cancellationToken: ct);
            return (chatResponse.Value.Content[0].Text ?? string.Empty, "stop");
        }

        if (_settingsService != null)
        {
            var providerName = settings?.ActiveProvider ?? "bilinmiyor";
            var modelName = settings != null ? GetTargetModel(settings) : "bilinmiyor";
            throw new InvalidOperationException(
                $"Seçili yapay zeka sağlayıcısından ('{providerName}', Model: '{modelName}') yanıt alınamadı. " +
                "Lütfen sağ üstteki 'API Ayarları' menüsünden girdiğiniz API anahtarını, bağlantıyı ve model seçimini kontrol edin.");
        }

        _logger.LogWarning("No AI API key (ANTHROPIC_API_KEY, GEMINI_API_KEY, OPENAI_API_KEY) configured. Returning mock response.");
        return ("Mock Claude Response", "stop");
    }

    public static string CompactPromptForGroq(string prompt, int maxChars)
    {
        if (string.IsNullOrEmpty(prompt) || prompt.Length <= maxChars)
            return prompt;

        // 1. Revizyon veya QC çağrısında incelenen doküman/içerik gövdesini koru, yalnız kaynak bloklarını sıkıştır
        int revRefIdx = prompt.IndexOf("KAYNAK BAĞLAMI", StringComparison.Ordinal);
        int revRulesIdx = prompt.IndexOf("ÇOK KRİTİK VE KESİN KURALLAR:", revRefIdx > 0 ? revRefIdx : 0, StringComparison.Ordinal);
        if (revRefIdx > 0 && revRulesIdx > revRefIdx)
        {
            int colonIdx = prompt.IndexOf(':', revRefIdx);
            int headerEnd = colonIdx > revRefIdx ? colonIdx + 1 : revRefIdx + "KAYNAK BAĞLAMI".Length;
            string beforeRef = prompt.Substring(0, headerEnd);
            string afterRef = prompt.Substring(revRulesIdx);
            int availForRef = Math.Max(800, maxChars - beforeRef.Length - afterRef.Length);
            string refBody = prompt.Substring(beforeRef.Length, revRulesIdx - beforeRef.Length);
            if (refBody.Length > availForRef)
            {
                refBody = "\n" + CompactSourcesProportionally(refBody, availForRef);
            }
            return beforeRef + refBody + afterRef;
        }

        // 2. [K#], [KAYNAK #, [KAYNAK: bloklarını algıla ve tüm kaynakları orantılı sıkıştır (kalan kaynakları koru!)
        string[] sourceMarkers = { "=== [K", "[KAYNAK #", "[KAYNAK:" };
        int firstSourceIdx = -1;
        foreach (var marker in sourceMarkers)
        {
            int idx = prompt.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0 && (firstSourceIdx < 0 || idx < firstSourceIdx))
                firstSourceIdx = idx;
        }

        if (firstSourceIdx >= 0)
        {
            string prefix = prompt.Substring(0, firstSourceIdx);

            int suffixStart = -1;
            foreach (var anchor in new[] { "ÇOK KRİTİK KURALLAR", "İKİ KATMANLI OTORİTE", "ÖNEMLİ FORMAT VE BÜTÜNLÜK", "ÖNEMLİ FORMAT", "Lütfen değerlendirme sonucunu", "ÖNCELİKLE:", "--- KONU VE BAŞLIK DÖKÜMÜ ---" })
            {
                int idx = prompt.IndexOf(anchor, firstSourceIdx, StringComparison.Ordinal);
                if (idx > firstSourceIdx && (suffixStart < 0 || idx < suffixStart))
                    suffixStart = idx;
            }

            string sourcesSection = suffixStart > firstSourceIdx
                ? prompt.Substring(firstSourceIdx, suffixStart - firstSourceIdx)
                : prompt.Substring(firstSourceIdx);
            string suffix = suffixStart > firstSourceIdx ? prompt.Substring(suffixStart) : string.Empty;

            bool hasProtectedBody = prefix.Contains("DOĞRULANMIŞ ARAŞTIRMA ÖZETİ", StringComparison.Ordinal) ||
                                    prefix.Contains("DENETLENECEK İÇERİK", StringComparison.Ordinal);
            int maxPrefix = hasProtectedBody
                ? Math.Min(prefix.Length, (int)(maxChars * 0.45))
                : Math.Min(prefix.Length, (int)(maxChars * 0.25));
            if (prefix.Length > maxPrefix && maxPrefix > 240)
            {
                prefix = prefix.Substring(0, maxPrefix - 140) + "\n[...]\n" + prefix.Substring(prefix.Length - 140);
            }

            int maxSuffix = Math.Max(1400, (int)(maxChars * 0.32));
            if (suffix.Length > maxSuffix)
            {
                int halfSuffix = maxSuffix / 2;
                suffix = suffix.Substring(0, halfSuffix) + "\n[...]\n" + suffix.Substring(suffix.Length - (maxSuffix - halfSuffix));
            }

            int remainingForSources = Math.Max(1400, maxChars - prefix.Length - suffix.Length);
            string compactedSources = CompactSourcesProportionally(sourcesSection, remainingForSources);
            var combined = prefix + compactedSources + suffix;
            return combined.Length <= maxChars ? combined : combined.Substring(0, maxChars);
        }

        // Fallback: Genel uzun metinlerde dağıtık baş + son koru
        int headChars = (int)(maxChars * 0.55);
        int tailChars = Math.Max(200, maxChars - headChars - 80);
        return prompt.Substring(0, headChars)
             + "\n\n[... Uzun içerik Groq token limiti nedeniyle kısaltıldı ...]\n\n"
             + prompt.Substring(prompt.Length - tailChars);
    }

    private static string CompactSourcesProportionally(string sourcesSection, int budgetChars)
    {
        if (string.IsNullOrEmpty(sourcesSection) || sourcesSection.Length <= budgetChars)
            return sourcesSection;

        string[] markers = { "=== [K", "[KAYNAK #", "[KAYNAK:" };
        var starts = new List<int>();
        for (int i = 0; i < sourcesSection.Length; i++)
        {
            foreach (var m in markers)
            {
                if (sourcesSection.AsSpan(i).StartsWith(m, StringComparison.Ordinal))
                {
                    starts.Add(i);
                    i += m.Length - 1;
                    break;
                }
            }
        }

        if (starts.Count <= 1)
        {
            return sourcesSection.Length > budgetChars
                ? sourcesSection.Substring(0, budgetChars) + "\n[... Kaynak kısaltıldı ...]\n"
                : sourcesSection;
        }

        int perBlockBudget = Math.Max(220, budgetChars / starts.Count);
        var sb = new StringBuilder(budgetChars + 256);

        for (int i = 0; i < starts.Count; i++)
        {
            if (sb.Length >= budgetChars) break;
            int s = starts[i];
            int e = i + 1 < starts.Count ? starts[i + 1] : sourcesSection.Length;
            var block = sourcesSection.Substring(s, e - s);

            if (block.Length <= perBlockBudget)
            {
                sb.AppendLine(block.TrimEnd());
            }
            else
            {
                int nl = block.IndexOf('\n');
                string header = nl > 0 && nl < 200 ? block.Substring(0, nl + 1) : string.Empty;
                string body = header.Length > 0 ? block.Substring(header.Length) : block;
                int bodyBudget = Math.Max(80, perBlockBudget - header.Length - 24);
                string clipped = body.Length > bodyBudget ? body.Substring(0, bodyBudget).TrimEnd() + " [...]" : body;
                sb.AppendLine(header + clipped);
            }
        }

        return sb.ToString();
    }

    public async Task<string> GenerateResearchSummaryAsync(string topic, string targetLength, string targetAudience, string contextData, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.SynthesisResearchPrompt, topic, NormalizeTargetLength(targetLength), targetAudience, contextData);
        var raw = await CallLlmAsync(prompt, ct);
        return NormalizeMarkdownOutput(raw, isVideoPlan: false);
    }

    public async Task<string> GenerateVideoPlanAsync(string topic, string targetLength, string contextData, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.SynthesisVideoPlanPrompt, topic, NormalizeTargetLength(targetLength), contextData);
        var raw = await CallLlmAsync(prompt, ct);
        var normalized = NormalizeMarkdownOutput(raw, isVideoPlan: true);

        int? parsedMinutes = PrompterPacingCalculator.TryParseTargetMinutes(targetLength);
        if (parsedMinutes.HasValue)
        {
            var budget = PrompterPacingCalculator.CalculateBudget(parsedMinutes.Value);
            normalized = await TightenBatchIfOverBudgetAsync(
                normalized,
                topic,
                $"Hedef Süre: {parsedMinutes.Value} dk — Tüm sahneler toplamda {budget.MinWords}–{budget.MaxWords} kelime aralığında olmalı.",
                budget,
                0,
                ct);
        }

        return normalized;
    }

    public async Task<string> GenerateGroundedVideoPlanAsync(string topic, string targetLength, string contextData, string researchSummary, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(researchSummary))
        {
            return await GenerateVideoPlanAsync(topic, targetLength, contextData, ct);
        }

        var prompt = string.Format(
            PromptTemplates.SynthesisVideoPlanGroundedPrompt,
            topic,
            NormalizeTargetLength(targetLength),
            contextData,
            researchSummary);
        var raw = await CallLlmAsync(prompt, ct);
        var normalized = NormalizeMarkdownOutput(raw, isVideoPlan: true);

        int? parsedMinutes = PrompterPacingCalculator.TryParseTargetMinutes(targetLength);
        if (parsedMinutes.HasValue)
        {
            var budget = PrompterPacingCalculator.CalculateBudget(parsedMinutes.Value);
            normalized = await TightenBatchIfOverBudgetAsync(
                normalized,
                topic,
                $"Hedef Süre: {parsedMinutes.Value} dk — Tüm sahneler toplamda {budget.MinWords}–{budget.MaxWords} kelime aralığında olmalı.",
                budget,
                0,
                ct);
        }

        return normalized;
    }

    public async Task<string> ExtractClaimsAsync(string text, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.ClaimExtractionPrompt, text);
        return await CallLlmAsync(prompt, ct, taskType: "qc");
    }

    public async Task<string> VerifyClaimAsync(string claim, string contextData, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.QcVerificationPrompt, claim, contextData);
        return await CallLlmAsync(prompt, ct, taskType: "qc");
    }

    public async Task<string> BatchQualityCheckAsync(string summaryText, string contextData, int claimCount = 8, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.BatchQcPrompt, claimCount, summaryText, contextData);
        return await CallLlmAsync(prompt, ct, taskType: "qc");
    }

    public async Task<string> ReviseContentAsync(string originalContent, string revisionInstruction, string contentType, string topic, string contextData = "", CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.RevisionPrompt, contentType, originalContent, revisionInstruction, topic, contextData);
        var raw = await CallLlmAsync(prompt, ct, taskType: "videoplan");
        bool isPlan = contentType.Contains("Plan", StringComparison.OrdinalIgnoreCase);
        return NormalizeMarkdownOutput(raw, isVideoPlan: isPlan);
    }

    public async Task<string> ReQualityCheckAsync(string currentContent, string previousQcReportJson, string contextData, int claimCount = 8, CancellationToken ct = default)
    {
        var prevReport = string.IsNullOrWhiteSpace(previousQcReportJson) ? "Önceki QC raporu bulunmuyor (İlk denetim)." : previousQcReportJson;
        var prompt = string.Format(PromptTemplates.ReQcVerificationPrompt, claimCount, currentContent, contextData, prevReport);
        return await CallLlmAsync(prompt, ct, taskType: "qc");
    }

    public async Task<string> AnalyzeTopicAsync(string topic, string allSourcesData, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.TopicAnalysisPrompt, topic, allSourcesData);
        return await CallLlmAsync(prompt, ct);
    }

    public async Task<string> GenerateSeriesPlanAsync(string topic, string targetAudience, string konuAnaliziJson, string allSourcesData, string userConstraintsJson = "", CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.SeriesPlanPrompt, topic, targetAudience, konuAnaliziJson, allSourcesData, userConstraintsJson);
        return await CallLlmAsync(prompt, ct);
    }

    public async Task<string> GenerateSeriesPlanAsync(
        string topicAndThesis,
        string targetAudience,
        string toneInstruction,
        string konuAnaliziJson,
        string structuredSourceCardsJson,
        string formattedConstraintsText,
        string previousPlanJson = "",
        CancellationToken ct = default)
    {
        var effectiveTone = string.IsNullOrWhiteSpace(toneInstruction)
            ? "Akademik derinliği koruyan, akıcı ve düşündürücü stüdyo anlatımı"
            : toneInstruction;
        var effectiveConstraints = string.IsNullOrWhiteSpace(formattedConstraintsText)
            ? "Özel kısıt belirtilmedi; konu analizindeki önerilen bölüm sayısı ve süreleri temel al."
            : formattedConstraintsText;
        var effectivePreviousPlan = string.IsNullOrWhiteSpace(previousPlanJson)
            ? "Önceki taslak bulunmuyor (İlk taslak üretimi)."
            : previousPlanJson;

        var prompt = string.Format(
            PromptTemplates.SeriesPlanRichPrompt,
            topicAndThesis,
            targetAudience,
            effectiveTone,
            konuAnaliziJson,
            structuredSourceCardsJson,
            effectiveConstraints,
            effectivePreviousPlan);

        return await CallLlmAsync(prompt, ct, taskType: "videoplan");
    }

    public async Task<(string ArastirmaOzeti, string VideoPlani, string DevirDefteriJson)> GenerateSeriesEpisodeAsync(
        SeriesEpisodeGenerationRequest request,
        Func<string, int, Task>? onProgress = null,
        CancellationToken ct = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Eğer bu bölüm daha önceki bir plan versiyonundaki (Plan v1) düzenlenmiş son dökümanı koruyarak
        // yeni kaynaklarla zenginleştirilecekse (Koruyucu Evrimsel Genişletme Modu), sıfırdan yazmak yerine
        // kullanıcının son metnini baz alarak sahne-sahne genişlet!
        if (!string.IsNullOrWhiteSpace(request.BazAlinanMevcutVideoPlani))
        {
            return await ExpandAnchoredEpisodeAsync(request, onProgress, ct);
        }

        int hedefSureDk = Math.Clamp(request.HedefSureDk > 0 ? request.HedefSureDk : 15, 3, 90);
        int sceneCount = PrompterPacingCalculator.CalculateTargetSceneCount(hedefSureDk);
        var episodeBudget = PrompterPacingCalculator.CalculateBudget(hedefSureDk);
        string episodeBudgetSummary = $"{episodeBudget.MinWords}–{episodeBudget.MaxWords} kelime (ideal ~{episodeBudget.TargetWords})";
        int toplamBolum = Math.Max(request.ToplamBolum, request.BolumNo);

        var authorThesisDirective = BuildEpisodeAuthorThesisDirective(request);
        var continuityText = string.IsNullOrWhiteSpace(request.OncekiBolumDevirDefteriJson)
            ? "Bu serinin 1. bölümüdür (Önceki bölüm devir defteri yok)."
            : request.OncekiBolumDevirDefteriJson;
        var effectiveAudience = string.IsNullOrWhiteSpace(request.HedefKitle) ? "Genel İzleyici" : request.HedefKitle;
        var effectiveHook = string.IsNullOrWhiteSpace(request.Kanca)
            ? $"{request.CalismaBasligi} konusunun temel paradoksu ve çarpıcı sorusu"
            : request.Kanca;

        // ─── AŞAMA 1: Kapsamlı Araştırma Özeti + HedefSureDk Uyumlu Sahne Planı ───
        if (onProgress != null)
        {
            await onProgress($"Bölüm {request.BolumNo}: Aşama 1/3 — Araştırma özeti ve {sceneCount} sahnelik zaman-kelime planı hazırlanıyor...", 15);
        }

        var stage1Prompt = string.Format(
            PromptTemplates.EpisodeStage1ResearchAndOutlinePrompt,
            request.BolumNo,
            toplamBolum,
            request.CalismaBasligi,
            effectiveHook,
            request.AnaFikir,
            hedefSureDk,
            sceneCount,
            episodeBudgetSummary,
            effectiveAudience,
            string.IsNullOrWhiteSpace(request.BolumKonulariJson) ? "[]" : request.BolumKonulariJson,
            string.IsNullOrWhiteSpace(request.SahneIskeletiJson) ? "[]" : request.SahneIskeletiJson,
            authorThesisDirective,
            continuityText,
            request.KaynakBaglamiText);

        var stage1Raw = await CallLlmAsync(stage1Prompt, ct, taskType: "videoplan");
        var (rawArastirmaOzeti, rawSahnePlaniJson) = LlmJson.ParseStage1ResearchAndOutline(stage1Raw);
        var arastirmaOzeti = NormalizeMarkdownOutput(rawArastirmaOzeti, isVideoPlan: false);

        if (string.IsNullOrWhiteSpace(arastirmaOzeti))
        {
            arastirmaOzeti = await GenerateResearchSummaryAsync(
                request.CalismaBasligi,
                $"{hedefSureDk} Dakika",
                effectiveAudience,
                $"{authorThesisDirective}\n\n{request.KaynakBaglamiText}",
                ct);
        }

        var plannedScenes = NormalizeAndEnforceSceneDurations(
            rawSahnePlaniJson,
            request.SahneIskeletiJson,
            request.BolumKonulariJson,
            request.CalismaBasligi,
            hedefSureDk,
            sceneCount);

        var allScenesOverviewJson = JsonSerializer.Serialize(plannedScenes, new JsonSerializerOptions { WriteIndented = true });

        if (onProgress != null)
        {
            await onProgress($"Bölüm {request.BolumNo}: Aşama 2/3 — {plannedScenes.Count} sahnelik derin çekim ve prompter metni yazılıyor...", 30);
        }

        // ─── AŞAMA 2: Sahne-Sahne Derin Yazım (<= 8 dk tek geçiş, > 8 dk 2'şerli iteratif gruplar) ───
        var videoPlanSb = new StringBuilder();
        videoPlanSb.AppendLine($"# {request.CalismaBasligi}");
        videoPlanSb.AppendLine($"**Video Tipi:** Uzun Formatlı Stüdyo Analizi / Seri Bölüm {request.BolumNo}/{toplamBolum}");
        videoPlanSb.AppendLine($"**Hedef Süre:** {hedefSureDk} Dakika ({episodeBudget.MinWords}–{episodeBudget.MaxWords} kelime, ideal ~{episodeBudget.TargetWords} kelime prompter konuşma metni — ~{PrompterPacingCalculator.TargetWordsPerMinute} kelime/dk)");
        if (!string.IsNullOrWhiteSpace(request.Kanca))
            videoPlanSb.AppendLine($"**Açılış Kancası:** {request.Kanca}");
        if (!string.IsNullOrWhiteSpace(request.AnaFikir))
            videoPlanSb.AppendLine($"**Bölüm Ana Tezi:** {request.AnaFikir}");
        videoPlanSb.AppendLine();
        videoPlanSb.AppendLine("---");
        videoPlanSb.AppendLine();

        int batchSize = hedefSureDk <= 8 ? plannedScenes.Count : 2;
        string previousClosingContext = "Bu ilk sahne grubudur (Sahne 1). İzleyiciyi selamlama klişesine boğmadan doğrudan güçlü Açılış Kancası ile sahneye gir.";

        var researchRefForScenes = arastirmaOzeti.Length > 14000
            ? arastirmaOzeti.Substring(0, 14000) + "\n[... Araştırma özeti devamı kaynak bağlamındadır ...]"
            : arastirmaOzeti;

        for (int i = 0; i < plannedScenes.Count; i += batchSize)
        {
            var batch = plannedScenes.Skip(i).Take(batchSize).ToList();
            int firstNo = batch.First().SahneNo;
            int lastNo = batch.Last().SahneNo;
            string groupLabel = firstNo == lastNo
                ? $"Sahne {firstNo} / {plannedScenes.Count}"
                : $"Sahne {firstNo}-{lastNo} / {plannedScenes.Count}";

            int batchMinutes = Math.Max(1, batch.Sum(s => s.TahminiSureDk));
            var batchBudget = PrompterPacingCalculator.CalculateBudget(batchMinutes);
            string batchBudgetRangeText = $"{batchBudget.MinWords}–{batchBudget.MaxWords} kelime (ideal ~{batchBudget.TargetWords} kelime; {batchBudget.MaxWords} kelimeyi KESİNLİKLE AŞMA!)";

            var batchTargetDesc = FormatTargetBatchInstructions(batch, plannedScenes.Count);

            var batchPrompt = string.Format(
                PromptTemplates.EpisodeStage2SceneBatchPrompt,
                request.BolumNo,
                request.CalismaBasligi,
                hedefSureDk,
                effectiveAudience,
                groupLabel,
                authorThesisDirective,
                continuityText,
                allScenesOverviewJson,
                researchRefForScenes,
                previousClosingContext,
                batchTargetDesc,
                request.KaynakBaglamiText,
                batchBudgetRangeText);

            var batchMarkdownRaw = await CallLlmAsync(batchPrompt, ct, taskType: "videoplan");
            var cleanedBatch = CleanSceneBatchOutput(batchMarkdownRaw, request.CalismaBasligi);
            cleanedBatch = await TightenBatchIfOverBudgetAsync(
                cleanedBatch,
                groupLabel,
                batchTargetDesc,
                batchBudget,
                request.BolumNo,
                ct);

            if (!string.IsNullOrWhiteSpace(cleanedBatch))
            {
                videoPlanSb.AppendLine(cleanedBatch.Trim());
                videoPlanSb.AppendLine();
                if (i + batchSize < plannedScenes.Count)
                {
                    videoPlanSb.AppendLine("---");
                    videoPlanSb.AppendLine();
                }

                previousClosingContext = ExtractClosingContextForNextBatch(cleanedBatch, lastNo);
            }

            int completedScenes = Math.Min(i + batch.Count, plannedScenes.Count);
            int pct = 30 + (int)Math.Round((double)completedScenes / plannedScenes.Count * 48.0);
            if (onProgress != null)
            {
                await onProgress($"Bölüm {request.BolumNo}: {groupLabel} yazıldı ({completedScenes}/{plannedScenes.Count} sahne tamamlandı)...", pct);
            }
        }

        var fullVideoPlan = NormalizeMarkdownOutput(videoPlanSb.ToString(), isVideoPlan: true);

        // ─── AŞAMA 3: Yapılandırılmış Bölüm Devir Defteri (EpisodeContinuityLedgerDto) ───
        if (onProgress != null)
        {
            await onProgress($"Bölüm {request.BolumNo}: Aşama 3/3 — Sonraki bölüm için devir defteri çıkarılıyor...", 85);
        }

        string devirDefteriJson;
        try
        {
            var planTailForLedger = fullVideoPlan.Length > 10000
                ? fullVideoPlan.Substring(0, 4000) + "\n...\n" + fullVideoPlan.Substring(fullVideoPlan.Length - 6000)
                : fullVideoPlan;
            var summaryForLedger = arastirmaOzeti.Length > 10000
                ? arastirmaOzeti.Substring(0, 10000)
                : arastirmaOzeti;

            var stage3Prompt = string.Format(
                PromptTemplates.EpisodeStage3ContinuityLedgerPrompt,
                request.BolumNo,
                request.CalismaBasligi,
                request.BolumNo + 1,
                summaryForLedger,
                planTailForLedger);

            var rawLedger = await CallLlmAsync(stage3Prompt, ct, taskType: "videoplan");
            devirDefteriJson = LlmJson.NormalizeContinuityLedgerJson(
                rawLedger,
                request.BolumNo,
                request.CalismaBasligi,
                arastirmaOzeti,
                fullVideoPlan);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Bölüm {BolumNo} devir defteri LLM çağrısı başarısız oldu, yapılandırılmış fallback üretiliyor.", request.BolumNo);
            devirDefteriJson = LlmJson.NormalizeContinuityLedgerJson(
                null,
                request.BolumNo,
                request.CalismaBasligi,
                arastirmaOzeti,
                fullVideoPlan);
        }

        if (onProgress != null)
        {
            await onProgress($"Bölüm {request.BolumNo}: Tüm senaryo ve devir defteri tamamlandı.", 90);
        }

        return (arastirmaOzeti, fullVideoPlan, devirDefteriJson);
    }

    private async Task<(string ArastirmaOzeti, string VideoPlani, string DevirDefteriJson)> ExpandAnchoredEpisodeAsync(
        SeriesEpisodeGenerationRequest request,
        Func<string, int, Task>? onProgress,
        CancellationToken ct)
    {
        var authorThesisDirective = BuildEpisodeAuthorThesisDirective(request);

        if (onProgress != null)
        {
            await onProgress($"Bölüm {request.BolumNo}: Mevcut düzenlenmiş metin korunarak Araştırma Özeti yeni kaynaklarla genişletiliyor...", 20);
        }

        string expandedOzet;
        if (!string.IsNullOrWhiteSpace(request.BazAlinanMevcutArastirmaOzeti))
        {
            var ozetPrompt = $$"""
                Sen VideoÖzet AI Stüdyosunun Kıdemli Akademik Editörüsün.
                Kullanıcı mevcut seriye yeni kaynaklar ekledi ve önceki plan versiyonundaki ({{request.CalismaBasligi}}) Araştırma Özetinin BÜYÜK ÖLÇÜDE KORUNARAK yalnızca yeni kaynaklardaki ilgili bulgularla zenginleştirilmesini istedi.

                MUTLAK KORUMA KURALLARI:
                1. Aşağıdaki `MEVCUT ARAŞTIRMA ÖZETİ` metnindeki başlıkları, paragrafları ve argümanları SİLME, KISALTMA veya BAŞTAN YAZMA (%90 aynen koru)!
                2. Yalnızca `YENİ VE GÜNCEL KAYNAK KANITLARI ([K#])` içinde olup mevcut özette henüz yer almayan yeni düşünür/hoca görüşlerini, örnekleri ve kavramları uygun alt başlıkların içine (veya gerekirse yeni bir alt başlıkla) doğal biçimde EKLE ve `[K#]` referanslarını belirt.
                3. Çıktı olarak YALNIZCA güncellenmiş tam Araştırma Özeti Markdown metnini döndür (açıklama veya JSON zarfı yazma).

                --- YAZAR TEZİ VE BÖLÜM DİREKTİFİ ---
                {{authorThesisDirective}}
                Güncel Bölüm Konuları: {{request.BolumKonulariJson}}

                --- KORUNACAK MEVCUT ARAŞTIRMA ÖZETİ ---
                {{request.BazAlinanMevcutArastirmaOzeti}}

                --- YENİ VE GÜNCEL KAYNAK KANITLARI ([K#]) ---
                {{request.KaynakBaglamiText}}
                """;

            var rawOzet = await CallLlmAsync(ozetPrompt, ct, taskType: "videoplan");
            expandedOzet = NormalizeMarkdownOutput(rawOzet, isVideoPlan: false);
            if (string.IsNullOrWhiteSpace(expandedOzet) || expandedOzet.Length < request.BazAlinanMevcutArastirmaOzeti.Length / 2)
                expandedOzet = request.BazAlinanMevcutArastirmaOzeti;
        }
        else
        {
            expandedOzet = await GenerateResearchSummaryAsync(
                request.CalismaBasligi,
                $"{Math.Max(10, request.HedefSureDk)} Dakika",
                string.IsNullOrWhiteSpace(request.HedefKitle) ? "Genel İzleyici" : request.HedefKitle,
                $"{authorThesisDirective}\n\n{request.KaynakBaglamiText}",
                ct);
        }

        // ─── AŞAMA 2 (KORUYUCU + PLAN UYUMLU): Onaylanmış Yeni Planın (Plan v2) Sahnelerini, Plan v1 Metnini Koruyarak Yaz ───
        int hedefSureDk = Math.Max(4, request.HedefSureDk);
        int toplamBolum = Math.Max(request.BolumNo, request.ToplamBolum);
        var episodeBudget = PrompterPacingCalculator.CalculateBudget(hedefSureDk);
        int fallbackSceneCount = PrompterPacingCalculator.CalculateTargetSceneCount(hedefSureDk);

        var plannedScenes = NormalizeAndEnforceSceneDurations(
            request.SahneIskeletiJson,
            request.SahneIskeletiJson,
            request.BolumKonulariJson,
            request.CalismaBasligi,
            hedefSureDk,
            fallbackSceneCount);

        var allScenesOverviewJson = JsonSerializer.Serialize(plannedScenes, new JsonSerializerOptions { WriteIndented = true });
        var basePlan = request.BazAlinanMevcutVideoPlani.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var baseScriptPool = basePlan.Length > 26000 ? basePlan.Substring(0, 26000) : basePlan;

        var expandedPlanBuilder = new StringBuilder();
        expandedPlanBuilder.AppendLine($"# Bölüm {request.BolumNo}: {request.CalismaBasligi}");
        expandedPlanBuilder.AppendLine($"**Video Tipi:** Uzun Formatlı Stüdyo Analizi / Seri Bölüm {request.BolumNo}/{toplamBolum}");
        expandedPlanBuilder.AppendLine($"**Hedef Süre:** ~{hedefSureDk} Dakika ({episodeBudget.MinWords}–{episodeBudget.MaxWords} kelime, ideal ~{episodeBudget.TargetWords} kelime prompter konuşma metni — ~{PrompterPacingCalculator.TargetWordsPerMinute} kelime/dk)");
        if (!string.IsNullOrWhiteSpace(request.Kanca))
            expandedPlanBuilder.AppendLine($"**Açılış Kancası:** {request.Kanca}");
        if (!string.IsNullOrWhiteSpace(request.AnaFikir))
            expandedPlanBuilder.AppendLine($"**Bölüm Ana Tezi:** {request.AnaFikir}");
        expandedPlanBuilder.AppendLine();
        expandedPlanBuilder.AppendLine("---");
        expandedPlanBuilder.AppendLine();

        int batchSize = 2;
        int totalBatches = (int)Math.Ceiling(plannedScenes.Count / (double)batchSize);
        string previousClosingContext = "Bu ilk sahne grubudur (Sahne 1). İzleyiciyi selamlama klişesine boğmadan doğrudan güçlü Açılış Kancası ile sahneye gir.";

        for (int bIdx = 0; bIdx < totalBatches; bIdx++)
        {
            var batch = plannedScenes.Skip(bIdx * batchSize).Take(batchSize).ToList();
            int firstNo = batch.First().SahneNo;
            int lastNo = batch.Last().SahneNo;
            bool isLast = bIdx == totalBatches - 1;
            string groupLabel = firstNo == lastNo
                ? $"Sahne {firstNo} / {plannedScenes.Count}"
                : $"Sahne {firstNo}-{lastNo} / {plannedScenes.Count}";

            if (onProgress != null)
            {
                int pct = 35 + (int)Math.Round(((bIdx + 1.0) / totalBatches) * 45.0);
                await onProgress($"Bölüm {request.BolumNo}: {groupLabel} — Yeni plan sahneleri ve mevcut metin sentezleniyor...", pct);
            }

            var batchTargetDesc = FormatTargetBatchInstructions(batch, plannedScenes.Count);
            int batchMinutes = Math.Max(1, batch.Sum(s => s.TahminiSureDk));
            var batchBudget = PrompterPacingCalculator.CalculateBudget(batchMinutes);

            var batchPrompt = BuildAnchoredSceneBatchPrompt(
                request,
                authorThesisDirective,
                groupLabel,
                batchTargetDesc,
                batchBudget,
                allScenesOverviewJson,
                previousClosingContext,
                baseScriptPool,
                isLastBatch: isLast);

            var rawBatch = await CallLlmAsync(batchPrompt, ct, taskType: "videoplan");
            var cleanedBatch = CleanSceneBatchOutput(rawBatch, request.CalismaBasligi);
            cleanedBatch = await TightenBatchIfOverBudgetAsync(
                cleanedBatch,
                groupLabel,
                batchTargetDesc,
                batchBudget,
                request.BolumNo,
                ct);

            if (!string.IsNullOrWhiteSpace(cleanedBatch))
            {
                expandedPlanBuilder.AppendLine(cleanedBatch.Trim());
                expandedPlanBuilder.AppendLine();
                if (!isLast)
                {
                    expandedPlanBuilder.AppendLine("---");
                    expandedPlanBuilder.AppendLine();
                }

                previousClosingContext = ExtractClosingContextForNextBatch(cleanedBatch, lastNo);
            }
        }

        var fullVideoPlan = NormalizeMarkdownOutput(expandedPlanBuilder.ToString(), isVideoPlan: true);

        string devirDefteriJson;
        try
        {
            var summaryForLedger = expandedOzet.Length > 8000 ? expandedOzet.Substring(0, 8000) : expandedOzet;
            var planTailForLedger = fullVideoPlan.Length > 8000 ? fullVideoPlan.Substring(fullVideoPlan.Length - 8000) : fullVideoPlan;
            var stage3Prompt = string.Format(
                PromptTemplates.EpisodeStage3ContinuityLedgerPrompt,
                request.BolumNo,
                request.CalismaBasligi,
                request.BolumNo + 1,
                summaryForLedger,
                planTailForLedger);

            var rawLedger = await CallLlmAsync(stage3Prompt, ct, taskType: "videoplan");
            devirDefteriJson = LlmJson.NormalizeContinuityLedgerJson(
                rawLedger,
                request.BolumNo,
                request.CalismaBasligi,
                expandedOzet,
                fullVideoPlan);
        }
        catch
        {
            devirDefteriJson = LlmJson.NormalizeContinuityLedgerJson(
                null,
                request.BolumNo,
                request.CalismaBasligi,
                expandedOzet,
                fullVideoPlan);
        }

        if (onProgress != null)
            await onProgress($"Bölüm {request.BolumNo}: Koruyucu genişletme tamamlandı.", 90);

        return (expandedOzet, fullVideoPlan, devirDefteriJson);
    }

    private static string BuildAnchoredSceneBatchPrompt(
        SeriesEpisodeGenerationRequest request,
        string authorThesisDirective,
        string groupLabel,
        string batchTargetDesc,
        PrompterWordBudget batchBudget,
        string allScenesOverviewJson,
        string previousClosingContext,
        string existingSavedScriptPool,
        bool isLastBatch)
    {
        var closingRule = isLastBatch
            ? $"Bu grup bölümün SON sahnesini içermektedir. Kapanış geçişinde YALNIZCA şu kurala uy: {request.SonrakiBolumOzeti}"
            : "Yalnızca aşağıda belirtilen sahne grubunu yaz; sonraki sahnelere geçme ve bölümü erken kapatma.";

        return $$"""
            Sen VideoÖzet AI Stüdyosunun Baş Senaryo Editörüsün.
            Şu anda **Bölüm {{request.BolumNo}}: {{request.CalismaBasligi}}** senaryosunun **{{groupLabel}}** grubunu yazıyorsun.

            İKİLİ TEMEL GÖREV (HEM YENİ PLANA %100 UYUM HEM MEVCUT CÜMLE KORUMASI):
            1. **ONAYLANMIŞ YENİ PLAN İSKELETİNE (%100 ZORUNLU UYUM):**
               Aşağıdaki `BU GRUPTA YAZILACAK YENİ PLAN SAHNELERİ` listesinde yer alan sahne numaralarını, sahne başlıklarını (`## Sahne X: ...`), kesin zaman aralıklarını, kesin kelime bütçesini (bu grup için toplam prompter metni KESİNLİKLE **{{batchBudget.MinWords}}–{{batchBudget.MaxWords}} kelime** aralığında, ideal ~**{{batchBudget.TargetWords}} kelime** olmalıdır — ~105 kelime/dk Türkçe stüdyo temposu; **{{batchBudget.MaxWords}} kelime üst sınırını KESİNLİKLE AŞMA!**) ve sahnede işlenmesi emredilen **tüm yeni argümanları, düşünürleri, kavramları ve temsilleri** EKSİKSİZ İŞLE! Eski metinle tıpatıp aynı kalıp yeni plandaki eklemeleri atlamak kadar metni dolgu cümlelerle şişirmek de YASAKTIR!
            2. **MEVCUT KAYDEDİLMİŞ METNİN CÜMLELERİNİ VE ÜSLUBUNU KORUYARAK ENTEGRE ET:**
               Aşağıdaki `KORUNACAK MEVCUT SENARYO HAVUZU (Önceki Versiyondan Kaydedilen Son Metin)` içinde bu sahnenin konularını anlatan mevcut paragraflar ve cümleler varsa; yazarın özenle düzenlediği o cümleleri, akıcı stüdyo dilini ve kurguyu SİLME veya sıfırdan bozma! O mevcut cümleleri sahnenin omurgası olarak koru; yeni planın ve `YENİ VE GÜNCEL KAYNAK KANITLARI ([K#])` bloğunun getirdiği yeni argümanları, ince kelâmî/felsefi ayrımları ve somut örnekleri (dış kaynak konuşmacı/kanal ismi anmadan, kendi özgün stüdyo dilimizle) mevcut paragrafların içine ve devamına pürüzsüz geçişlerle ör!
            3. {{closingRule}}

            KESİN VE DEĞİŞTİRİLEMEZ MARKDOWN FORMAT ŞABLONU (ASLA DÜZ MAKALE VEYA `## Sonuç` YAZMA!):
            Bu gruptaki her sahneyi YALNIZCA aşağıdaki şablonda üret (köşeli parantez KULLANMA, iki sahne arasına `---` koy):

            ## Sahne X: Sahne Başlığı
            **Zaman Aralığı:** MM:SS - MM:SS

            ### 1. Görsel & Edit Yönergesi
            - **Kamera Açısı & Kadraj:** ...
            - **Görsel, B-Roll ve Kurgu:** ...
            - **Metin Grafiği (On-Screen Text):** ...
            - **Müzik & Ses Efekti:** ...

            ### 2. Prompter Metni (Aynen Okunacak)
            > İlk konuşma paragrafı...
            >
            > İkinci konuşma paragrafı...
            >
            > Üçüncü konuşma paragrafı...

            --- BÖLÜM VE YAZAR DİREKTİFLERİ ---
            {{authorThesisDirective}}

            --- ÖNCEKİ SAHNEDEN DEVİR / GEÇİŞ BAĞLAMI ---
            {{previousClosingContext}}

            --- TÜM BÖLÜM SAHNE HARİTASI (KONU TEKRARINI ÖNLEMEK İÇİN) ---
            {{allScenesOverviewJson}}

            --- BU GRUPTA YAZILACAK YENİ PLAN SAHNELERİ (KESİN UYGULANACAK!) ---
            {{batchTargetDesc}}

            --- KORUNACAK MEVCUT SENARYO HAVUZU (CÜMLE VE ÜSLUBU KORUNACAK BAZ METİN) ---
            {{existingSavedScriptPool}}

            --- YENİ VE GÜNCEL KAYNAK KANITLARI ([K#]) ---
            {{request.KaynakBaglamiText}}
            """;
    }

    public async Task<string> GenerateSeriesVideoContentAsync(
        string bolumBasligi,
        string bolumKonulariJson,
        string oncekiVideoDevirNotuJson,
        string targetAudience,
        string contextData,
        string userInstructions = "",
        CancellationToken ct = default)
    {
        var req = new SeriesEpisodeGenerationRequest
        {
            BolumNo = 1,
            ToplamBolum = 1,
            CalismaBasligi = bolumBasligi,
            AnaFikir = bolumBasligi,
            HedefSureDk = 12,
            BolumKonulariJson = bolumKonulariJson ?? "[]",
            OncekiBolumDevirDefteriJson = oncekiVideoDevirNotuJson ?? string.Empty,
            HedefKitle = targetAudience ?? "Genel",
            YazarTeziVeGenelOdak = userInstructions ?? string.Empty,
            KaynakBaglamiText = contextData ?? string.Empty
        };

        var (arastirmaOzeti, videoPlani, devirDefteriJson) = await GenerateSeriesEpisodeAsync(req, null, ct);

        return JsonSerializer.Serialize(new
        {
            ArastirmaOzeti = arastirmaOzeti,
            VideoPlani = videoPlani,
            DevirNotu = devirDefteriJson
        });
    }

    public async Task<string> ExtractSourceTopicsAsync(string kaynakAdi, string metin, int parcaNo, int parcaSayisi, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.SourceTopicExtractionPrompt, kaynakAdi, parcaNo, parcaSayisi, metin);
        return await CallLlmAsync(prompt, ct, taskType: "summary");
    }

    private static string BuildEpisodeAuthorThesisDirective(SeriesEpisodeGenerationRequest req)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(req.YazarTeziVeGenelOdak))
            sb.AppendLine($"- Serinin Ana Odak ve Yazar Tezi: {req.YazarTeziVeGenelOdak}");
        if (!string.IsNullOrWhiteSpace(req.AnaFikir))
            sb.AppendLine($"- Bu Bölümün Ana Tezi: {req.AnaFikir}");
        if (!string.IsNullOrWhiteSpace(req.Kanca))
            sb.AppendLine($"- Bu Bölümün Açılış Kancası: {req.Kanca}");
        if (!string.IsNullOrWhiteSpace(req.EkTonTalimati))
            sb.AppendLine($"- Ek Ton ve Üslup Talimatı: {req.EkTonTalimati}");
        if (!string.IsNullOrWhiteSpace(req.OnayTalimati))
            sb.AppendLine($"- Kullanıcının Plan Onay / Özel Direktifi: {req.OnayTalimati}");
        if (!string.IsNullOrWhiteSpace(req.SonrakiBolumOzeti))
            sb.AppendLine($"- SONRAKİ BÖLÜM (BÖLÜM {req.BolumNo + 1}) BİLGİSİ VE KAPANIŞ KURALI: {req.SonrakiBolumOzeti}");

        return sb.Length > 0
            ? sb.ToString().Trim()
            : $"- Bölüm Odak Noktası: {req.CalismaBasligi}";
    }

    private sealed class PlannedEpisodeScene
    {
        public int SahneNo { get; set; }
        public string Baslik { get; set; } = string.Empty;
        public int TahminiSureDk { get; set; }
        public string ZamanAraligi { get; set; } = string.Empty;
        public int MinKelime { get; set; }
        public int HedefKelime { get; set; }
        public int MaxKelime { get; set; }
        public string ParagrafOnerisi { get; set; } = string.Empty;
        public string IslenecekArgumanVeDusunurler { get; set; } = string.Empty;
        public string YazarTeziBaglantisi { get; set; } = string.Empty;
        public List<string> AtananKanitKodlari { get; set; } = new();
    }

    private static List<PlannedEpisodeScene> NormalizeAndEnforceSceneDurations(
        string stage1ScenesJson,
        string fallbackOutlineJson,
        string topicsJson,
        string episodeTitle,
        int hedefSureDk,
        int targetSceneCount)
    {
        var scenes = TryParseScenesFromJson(stage1ScenesJson);
        if (scenes.Count == 0)
        {
            scenes = TryParseScenesFromJson(fallbackOutlineJson);
        }

        if (scenes.Count == 0)
        {
            var topics = new List<string>();
            try
            {
                var parsed = LlmJson.Deserialize<List<string>>(topicsJson);
                if (parsed != null) topics.AddRange(parsed.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            catch { }

            for (int idx = 0; idx < targetSceneCount; idx++)
            {
                string title = idx == 0
                    ? $"Giriş ve Temel Paradoks – {episodeTitle}"
                    : idx == targetSceneCount - 1
                        ? "Genel Sentez, Yazar Tezi Değerlendirmesi ve Kapanış"
                        : (idx - 1 < topics.Count ? topics[idx - 1] : $"{episodeTitle} – {idx}. Analitik Boyut");

                scenes.Add(new PlannedEpisodeScene
                {
                    SahneNo = idx + 1,
                    Baslik = title,
                    TahminiSureDk = Math.Max(1, hedefSureDk / targetSceneCount),
                    IslenecekArgumanVeDusunurler = title,
                    YazarTeziBaglantisi = "Bölüm ana teziyle bütünleşik derin analiz"
                });
            }
        }

        // Hedef süreye uygun sahne sayısını (dakikayı aşmayacak şekilde) garanti et
        int effectiveMinSceneCount = Math.Min(targetSceneCount, Math.Max(1, hedefSureDk));
        while (scenes.Count < effectiveMinSceneCount)
        {
            var longest = scenes.OrderByDescending(s => s.TahminiSureDk).First();
            int idxLongest = scenes.IndexOf(longest);
            int halfA = Math.Max(1, longest.TahminiSureDk / 2);
            int halfB = Math.Max(1, longest.TahminiSureDk - halfA);
            longest.TahminiSureDk = halfA;
            scenes.Insert(idxLongest + 1, new PlannedEpisodeScene
            {
                Baslik = $"{longest.Baslik} – Derinleştirilmiş Vaka ve Sentez",
                TahminiSureDk = halfB,
                IslenecekArgumanVeDusunurler = longest.IslenecekArgumanVeDusunurler,
                YazarTeziBaglantisi = longest.YazarTeziBaglantisi,
                AtananKanitKodlari = new List<string>(longest.AtananKanitKodlari)
            });
        }

        // sum(Delta t) == HedefSureDk eşitliğini deterministik olarak zorla!
        foreach (var s in scenes)
        {
            if (s.TahminiSureDk < 1) s.TahminiSureDk = 1;
        }

        int currentSum = scenes.Sum(s => s.TahminiSureDk);
        if (currentSum != hedefSureDk && scenes.Count > 0)
        {
            if (scenes.Count <= hedefSureDk)
            {
                double scale = (double)hedefSureDk / currentSum;
                foreach (var s in scenes)
                {
                    s.TahminiSureDk = Math.Max(1, (int)Math.Round(s.TahminiSureDk * scale));
                }

                int diff = hedefSureDk - scenes.Sum(s => s.TahminiSureDk);
                int cursor = 0;
                while (diff != 0 && cursor < 200)
                {
                    // Ortadaki gelişme sahnelerine öncelik ver
                    int targetIdx = (cursor + 1) % scenes.Count;
                    if (diff > 0)
                    {
                        scenes[targetIdx].TahminiSureDk++;
                        diff--;
                    }
                    else if (scenes[targetIdx].TahminiSureDk > 1)
                    {
                        scenes[targetIdx].TahminiSureDk--;
                        diff++;
                    }
                    cursor++;
                }
            }
        }

        int elapsedMin = 0;
        for (int i = 0; i < scenes.Count; i++)
        {
            scenes[i].SahneNo = i + 1;
            int startMin = elapsedMin;
            int endMin = elapsedMin + scenes[i].TahminiSureDk;
            elapsedMin = endMin;
            scenes[i].ZamanAraligi = $"{startMin:D2}:00 - {endMin:D2}:00";

            var sceneBudget = PrompterPacingCalculator.CalculateBudget(scenes[i].TahminiSureDk);
            scenes[i].MinKelime = sceneBudget.MinWords;
            scenes[i].HedefKelime = sceneBudget.TargetWords;
            scenes[i].MaxKelime = sceneBudget.MaxWords;
            scenes[i].ParagrafOnerisi = sceneBudget.ParagraphGuidance;
        }

        return scenes;
    }

    private static List<PlannedEpisodeScene> TryParseScenesFromJson(string? rawJson)
    {
        var result = new List<PlannedEpisodeScene>();
        if (string.IsNullOrWhiteSpace(rawJson)) return result;

        try
        {
            var extracted = LlmJson.ExtractJson(rawJson);
            if (string.IsNullOrWhiteSpace(extracted) || !extracted.StartsWith("[")) return result;

            using var doc = JsonDocument.Parse(extracted, new JsonDocumentOptions { AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            int autoNo = 1;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var baslik = GetPropString(el, "Baslik", "baslik", "SahneBasligi");
                if (string.IsNullOrWhiteSpace(baslik)) continue;

                int sure = GetPropInt(el, 3, "TahminiSureDk", "tahminiSureDk", "SureDk");
                var arguman = GetPropString(el, "IslenecekArgumanVeDusunurler", "AciklamaVeArgumanlar", "aciklamaVeArgumanlar", "Aciklama");
                var tezBag = GetPropString(el, "YazarTeziBaglantisi", "yazarTeziBaglantisi");

                var kodlar = new List<string>();
                foreach (var prop in el.EnumerateObject())
                {
                    if ((string.Equals(prop.Name, "AtananKanitKodlari", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(prop.Name, "AtananKaynakKodlari", StringComparison.OrdinalIgnoreCase)) &&
                        prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var k in prop.Value.EnumerateArray())
                        {
                            var ks = k.ValueKind == JsonValueKind.String ? k.GetString() : k.ToString();
                            if (!string.IsNullOrWhiteSpace(ks)) kodlar.Add(ks.Trim());
                        }
                    }
                }

                result.Add(new PlannedEpisodeScene
                {
                    SahneNo = autoNo++,
                    Baslik = baslik.Trim(),
                    TahminiSureDk = Math.Max(1, sure),
                    IslenecekArgumanVeDusunurler = arguman,
                    YazarTeziBaglantisi = tezBag,
                    AtananKanitKodlari = kodlar
                });
            }
        }
        catch
        {
        }

        return result;
    }

    private static string GetPropString(JsonElement obj, params string[] names)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (names.Any(n => string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase)))
            {
                return prop.Value.ValueKind == JsonValueKind.String
                    ? (prop.Value.GetString() ?? string.Empty)
                    : prop.Value.ToString();
            }
        }
        return string.Empty;
    }

    private static int GetPropInt(JsonElement obj, int fallback, params string[] names)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (names.Any(n => string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase)))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int iv)) return iv;
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out double dv)) return (int)Math.Round(dv);
                if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out int sv)) return sv;
            }
        }
        return fallback;
    }

    private static string FormatTargetBatchInstructions(List<PlannedEpisodeScene> batch, int totalSceneCount)
    {
        var sb = new StringBuilder();
        foreach (var s in batch)
        {
            string roleNote = s.SahneNo == 1
                ? "[AÇILIŞ SAHNESİ — İzleyiciyi Kanca ile yakala]"
                : (s.SahneNo == totalSceneCount
                    ? "[KAPANIŞ / SENTEZ SAHNESİ — Tüm bölümü toparla ve sonraki bölüme köprü kur]"
                    : "[GELİŞME SAHNESİ — Önceki sahneden organik geçişle derinleş, tekrar giriş yapma]");

            sb.AppendLine($"- **## Sahne {s.SahneNo}: {s.Baslik}** {roleNote}");
            sb.AppendLine($"  * Kesin Zaman Aralığı: `{s.ZamanAraligi}` ({s.TahminiSureDk} dakika)");
            sb.AppendLine($"  * Prompter Kelime Bütçesi (KESİN ARALIK): **{s.MinKelime}–{s.MaxKelime} kelime** (ideal ~**{s.HedefKelime} kelime**, {s.ParagrafOnerisi}; **{s.MaxKelime} kelime üst sınırını KESİNLİKLE AŞMA!**)");
            if (!string.IsNullOrWhiteSpace(s.IslenecekArgumanVeDusunurler))
                sb.AppendLine($"  * İşlenecek Düşünürler, Argümanlar ve Örnekler: {s.IslenecekArgumanVeDusunurler}");
            if (!string.IsNullOrWhiteSpace(s.YazarTeziBaglantisi))
                sb.AppendLine($"  * [YAZAR-TEZİ] Bağlantısı: {s.YazarTeziBaglantisi}");
            if (s.AtananKanitKodlari.Count > 0)
                sb.AppendLine($"  * Öncelikli Kanıt Blokları: {string.Join(", ", s.AtananKanitKodlari)}");
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }

    private static string CleanSceneBatchOutput(string rawBatch, string episodeTitle)
    {
        if (string.IsNullOrWhiteSpace(rawBatch)) return string.Empty;

        var text = StripReasoningTags(rawBatch).Trim();
        if (text.StartsWith("```markdown", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(11).Trim();
        else if (text.StartsWith("```"))
            text = text.Substring(3).Trim();
        if (text.EndsWith("```"))
            text = text.Substring(0, text.Length - 3).Trim();

        // Model yanlışlıkla en üste "# Bölüm Başlığı" tekrar yazdıysa ve altında "## Sahne" varsa üstteki H1'i temizle
        var firstSceneIdx = text.IndexOf("## Sahne", StringComparison.OrdinalIgnoreCase);
        if (firstSceneIdx > 0 && text.StartsWith("# ", StringComparison.Ordinal))
        {
            text = text.Substring(firstSceneIdx).TrimStart();
        }

        // Prompter blockquote paragrafları arasına boş '>' satırı ekle
        text = Regex.Replace(
            text,
            @"(?m)^(>\s*\S.*)\r?\n(?=>\s*[""“”])",
            "$1\n>\n");

        return text.Trim();
    }

    private async Task<string> TightenBatchIfOverBudgetAsync(
        string markdown,
        string groupLabel,
        string batchTargetDesc,
        PrompterWordBudget budget,
        int bolumNo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return markdown;

        int actualWords = PrompterPacingCalculator.CountPrompterWords(markdown);
        if (!PrompterPacingCalculator.IsOverBudget(actualWords, budget))
            return markdown;

        _logger.LogWarning(
            "Bölüm {BolumNo} ({GroupLabel}): Prompter kelime sayısı ({Actual}) üst sınırı aştı (Max: {Max}, Hedef: {Target}). Otomatik sıkılaştırma uygulanıyor...",
            bolumNo, groupLabel, actualWords, budget.MaxWords, budget.TargetWords);

        try
        {
            var tightenPrompt = string.Format(
                PromptTemplates.EpisodeSceneBatchTightenPrompt,
                groupLabel,
                actualWords,
                budget.MinWords,
                budget.TargetWords,
                budget.MaxWords,
                batchTargetDesc,
                markdown);

            var rawTightened = await CallLlmAsync(tightenPrompt, ct, taskType: "videoplan");
            var cleanedTightened = CleanSceneBatchOutput(rawTightened, groupLabel);

            if (string.IsNullOrWhiteSpace(cleanedTightened))
                return markdown;

            bool hadScenes = markdown.Contains("## Sahne", StringComparison.OrdinalIgnoreCase);
            if (hadScenes && !cleanedTightened.Contains("## Sahne", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Bölüm {BolumNo} ({GroupLabel}): Sıkılaştırılmış çıktıda '## Sahne' başlıkları kaybolduğu için orijinal metin korunuyor.", bolumNo, groupLabel);
                return markdown;
            }

            int tightenedWords = PrompterPacingCalculator.CountPrompterWords(cleanedTightened);
            int minAcceptable = (int)Math.Round(budget.MinWords * 0.65);

            if (tightenedWords < actualWords && tightenedWords >= minAcceptable)
            {
                _logger.LogInformation(
                    "Bölüm {BolumNo} ({GroupLabel}): Prompter kelime sayısı {Actual} -> {Tightened} kelimeye sıkılaştırıldı (Hedef: {Min}-{Max}).",
                    bolumNo, groupLabel, actualWords, tightenedWords, budget.MinWords, budget.MaxWords);
                return cleanedTightened;
            }

            _logger.LogWarning(
                "Bölüm {BolumNo} ({GroupLabel}): Sıkılaştırma sonucu ({Tightened} kelime) kabul aralığı dışında kaldı, orijinal metin korunuyor.",
                bolumNo, groupLabel, tightenedWords);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Bölüm {BolumNo} ({GroupLabel}): Otomatik sıkılaştırma çağrısı başarısız oldu, orijinal metin korunuyor.", bolumNo, groupLabel);
        }

        return markdown;
    }

    private static string ExtractClosingContextForNextBatch(string batchMarkdown, int lastSceneNo)
    {
        if (string.IsNullOrWhiteSpace(batchMarkdown))
            return string.Empty;

        var quoteLines = batchMarkdown.Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith(">") && l.Length > 8)
            .ToList();

        if (quoteLines.Count > 0)
        {
            var tailQuotes = string.Join("\n", quoteLines.TakeLast(6));
            if (tailQuotes.Length > 900)
                tailQuotes = tailQuotes.Substring(tailQuotes.Length - 900);
            return $"Sahne {lastSceneNo} sonundaki kapanış prompter metni:\n{tailQuotes}";
        }

        return batchMarkdown.Length > 800
            ? batchMarkdown.Substring(batchMarkdown.Length - 800)
            : batchMarkdown;
    }

    public async Task<string> AskStudioQuestionAsync(string prompt, CancellationToken ct = default)
    {
        return await CallLlmAsync(prompt, ct, taskType: "videoplan");
    }
}
