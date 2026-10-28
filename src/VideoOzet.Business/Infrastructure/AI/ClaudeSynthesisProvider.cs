using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mscc.GenerativeAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
                    if (settings.ActiveProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
                    {
                        return string.IsNullOrWhiteSpace(settings.OpenRouterModel) ? "google/gemini-3.8-flash" : settings.OpenRouterModel;
                    }
                    if (settings.ActiveProvider.Equals("groq", StringComparison.OrdinalIgnoreCase))
                    {
                        return string.IsNullOrWhiteSpace(settings.GroqModel) ? "openai/gpt-oss-120b" : settings.GroqModel;
                    }
                    if (!string.IsNullOrWhiteSpace(settings.AntigravityProxyKey))
                    {
                        return settings.AntigravityProxyModel;
                    }
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

    private async Task<string> CallLlmAsync(string prompt, CancellationToken ct)
    {
        var (text, finishReason) = await CallLlmRawAsync(prompt, ct);
        text = StripReasoningTags(text);

        if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finishReason, "max_tokens", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("LLM yanıtı token sınırına çarptı (finish_reason={FinishReason}, len={Len}). Kaldığı yerden devam ettirme (continuation) çağrısı yapılıyor...", finishReason, text.Length);
            try
            {
                string tailSnippet = text.Length > 500 ? text.Substring(text.Length - 500) : text;
                string continuationPrompt = $"""
                    Ürettiğin doküman çıktı token sınırı nedeniyle aşağıdaki metnin sonunda yarım kaldı:

                    --- YARIM KALAN SON KISIM ---
                    {tailSnippet}
                    --- SON ---

                    GÖREV: Yukarıdaki son cümlenin kaldığı yerden HİÇBİR BAŞLIĞI VEYA PARAGRAFI TEKRARLAMADAN doğrudan devam et ve dokümanı Sonuç / Kapanış bölümüne kadar aynı Markdown formatıyla eksiksiz tamamla.
                    """;
                var (contText, _) = await CallLlmRawAsync(continuationPrompt, ct);
                contText = StripReasoningTags(contText);
                if (!string.IsNullOrWhiteSpace(contText))
                {
                    text = text.TrimEnd() + "\n" + contText.TrimStart();
                }
            }
            catch (Exception contEx)
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

        var cleaned = StripReasoningTags(text);

        // Eğer JSON çıktısıysa dokunma
        if (cleaned.StartsWith("{") || cleaned.StartsWith("["))
            return cleaned;

        // Başlık veya içindekiler listesindeki köşeli parantez kalıntılarını temizle: "## 2. [Başlık]" -> "## 2. Başlık", "- 2. [Başlık]" -> "- 2. Başlık"
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"(?m)^(\s*(?:#{1,4}|[-*])\s*(?:\d+(?:\.\d+)*\.?\s*)?)\[([^\]\r\n]+)\]\s*$",
            "$1$2");

        // Sahne başlığındaki köşeli parantezleri temizle: "## Sahne 1: [Başlık]" -> "## Sahne 1: Başlık"
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"(?m)^(\s*#{1,4}\s*Sahne\s*\d+\s*[:–-]\s*)\[([^\]\r\n]+)\]",
            "$1$2");

        // Hedef süre normalize: "**Hedef Süre:** uzun" -> "**Hedef Süre:** ~15-20 Dakika (Uzun ve Detaylı)"
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"(?mi)^(\*\*Hedef Süre:\*\*\s*)uzun\s*$",
            "$1~15-20 Dakika (Uzun ve Detaylı)");
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"(?mi)^(\*\*Hedef Süre:\*\*\s*)orta\s*$",
            "$1~10-12 Dakika (Orta)");
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"(?mi)^(\*\*Hedef Süre:\*\*\s*)k[ıi]sa\s*$",
            "$1~5-8 Dakika (Kısa)");

        // Prompter blockquote paragrafları arasına boş '>' satırı ekle
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"(?m)^(>\s*\S.*)\r?\n(?=>\s*[""“”])",
            "$1\n>\n");

        if (isVideoPlan)
        {
            // Eğer Video Planı'nın başına yanlışlıkla Araştırma Özeti eklenmişse kırp
            int idxPlan = cleaned.IndexOf("# Video Planı", StringComparison.OrdinalIgnoreCase);
            if (idxPlan < 0) idxPlan = cleaned.IndexOf("## Sahne 1", StringComparison.OrdinalIgnoreCase);
            if (idxPlan > 0 && cleaned.Substring(0, idxPlan).Contains("Araştırma Özeti", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(idxPlan).TrimStart();
            }
        }
        else
        {
            // Eğer Araştırma Özeti'nin sonuna yanlışlıkla Video Planı eklenmişse kırp
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

        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"<think>.*?</think>",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        return cleaned;
    }

    private static string NormalizeTargetLength(string targetLength)
    {
        if (string.IsNullOrWhiteSpace(targetLength))
            return "10-15 Dakika (Detaylı)";

        return targetLength.Trim().ToLowerInvariant() switch
        {
            "kisa" or "kısa" => "~5-8 Dakika (Kısa)",
            "orta" => "~10-12 Dakika (Orta)",
            "uzun" => "~15-20 Dakika (Uzun ve Detaylı)",
            _ => targetLength
        };
    }

    private async Task<(string Text, string FinishReason)> CallLlmRawAsync(string prompt, CancellationToken ct)
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

        // 1. OpenRouter seçiliyse OpenRouter API'ye gönder
        if (settings != null &&
            settings.ActiveProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
        {
            var openRouterUrl = $"{settings.OpenRouterBaseUrl.TrimEnd('/')}/chat/completions";
            var openRouterModel = string.IsNullOrWhiteSpace(settings.OpenRouterModel) ? "google/gemini-3.8-flash" : settings.OpenRouterModel;
            var orKeys = settings.OpenRouterApiKey.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                  .Select(k => k.Trim())
                                                  .ToArray();

            Exception? lastOrEx = null;
            int maxOrRetries = Math.Max(orKeys.Length, 3);

            for (int tryCount = 0; tryCount < maxOrRetries; tryCount++)
            {
                var keyToUse = orKeys[tryCount % orKeys.Length];
                try
                {
                    using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                    var requestBody = new
                    {
                        model = openRouterModel,
                        messages = new[] { new { role = "user", content = prompt } },
                        max_tokens = 16384,
                        temperature = 0.2
                    };
                    var jsonContent = new System.Net.Http.StringContent(
                        System.Text.Json.JsonSerializer.Serialize(requestBody),
                        System.Text.Encoding.UTF8,
                        "application/json");

                    using var requestMessage = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, openRouterUrl);
                    requestMessage.Headers.Add("Authorization", $"Bearer {keyToUse}");
                    requestMessage.Headers.Add("HTTP-Referer", "https://videoozet.local");
                    requestMessage.Headers.Add("X-Title", "VideoOzet");
                    requestMessage.Content = jsonContent;

                    var response = await httpClient.SendAsync(requestMessage, ct);
                    var responseString = await response.Content.ReadAsStringAsync(ct);

                    if (response.IsSuccessStatusCode)
                    {
                        using var jsonDoc = System.Text.Json.JsonDocument.Parse(responseString);
                        var root = jsonDoc.RootElement;
                        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var firstChoice = choices[0];
                            var finishReason = firstChoice.TryGetProperty("finish_reason", out var fr) ? (fr.GetString() ?? "stop") : "stop";
                            if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                            {
                                return (content.GetString() ?? string.Empty, finishReason);
                            }
                        }
                    }

                    if (responseString.Contains("503") || responseString.Contains("429") || responseString.Contains("rate") || responseString.Contains("exhausted"))
                    {
                        throw new Exception($"High Demand or Limit: {responseString}");
                    }

                    throw new Exception($"OpenRouter API error ({response.StatusCode}): {responseString}");
                }
                catch (Exception ex) when (ex.Message.Contains("High Demand") || ex.Message.Contains("Limit") || ex.Message.Contains("503") || ex.Message.Contains("429"))
                {
                    lastOrEx = ex;
                    _logger.LogWarning("OpenRouter API call hit limit/demand on try {Try}. Error: {Error}", tryCount + 1, ex.Message);
                    if (tryCount < maxOrRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * (tryCount + 1)), ct);
                    }
                }
                catch (Exception ex)
                {
                    lastOrEx = ex;
                    _logger.LogWarning("OpenRouter modeli {Model} başarısız oldu ({Message}), OtoEdit yedek zincirine geçiliyor...", openRouterModel, ex.Message);
                    break;
                }
            }
        }

        var anthropicKey = _configuration["ANTHROPIC_API_KEY"];
        if (!string.IsNullOrWhiteSpace(anthropicKey))
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
                Temperature = 0.2m // Düşük sıcaklık, tutarlı ve halüsinasyonsuz çıktılar için
            };

            var response = await client.Messages.GetClaudeMessageAsync(parameters, ct);
            return ((response.Content[0] as TextContent)?.Text ?? string.Empty, response.StopReason ?? "stop");
        }

        // 2. Yerel Antigravity Proxy (yalnızca ActiveProvider 'antigravity' ise veya ayar servisi yoksa)
        bool allowLocalProxy = settings == null || settings.ActiveProvider.Equals("antigravity", StringComparison.OrdinalIgnoreCase);
        var geminiKey = allowLocalProxy
            ? (settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyKey)
                ? settings.AntigravityProxyKey
                : _configuration["GEMINI_API_KEY"])
            : null;

        if (!string.IsNullOrWhiteSpace(geminiKey))
        {
            var geminiModel = settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyModel)
                ? settings.AntigravityProxyModel
                : (_configuration["GEMINI_MODEL"] ?? "gemini-3.8-flash-tiered");
            var proxyBaseUrl = settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyUrl)
                ? settings.AntigravityProxyUrl.TrimEnd('/')
                : "http://localhost:8045/v1";

            var apiKeys = geminiKey.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(k => k.Trim())
                                   .ToArray();
            
            Exception lastException = null;
            int maxRetries = Math.Max(apiKeys.Length, 1);

            for (int tryCount = 0; tryCount < maxRetries; tryCount++)
            {
                var keyToUse = apiKeys[tryCount % apiKeys.Length];

                try
                {
                    using var httpClient = new System.Net.Http.HttpClient();
                    httpClient.Timeout = TimeSpan.FromMinutes(5);
                    
                    var requestBody = new
                    {
                        model = geminiModel,
                        messages = new[]
                        {
                            new { role = "user", content = prompt }
                        },
                        max_tokens = 16384,
                        temperature = 0.2
                    };
                    var jsonContent = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
                    var requestUrl = $"{proxyBaseUrl}/chat/completions";
                    
                    using var requestMessage = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, requestUrl);
                    requestMessage.Headers.Add("Authorization", $"Bearer {keyToUse}");
                    requestMessage.Content = jsonContent;
                    
                    var response = await httpClient.SendAsync(requestMessage, ct);
                    
                    var responseString = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        using var jsonDoc = System.Text.Json.JsonDocument.Parse(responseString);
                        var root = jsonDoc.RootElement;
                        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var firstChoice = choices[0];
                            var finishReason = firstChoice.TryGetProperty("finish_reason", out var fr) ? (fr.GetString() ?? "stop") : "stop";
                            if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                            {
                                var textResult = content.GetString();
                                return (textResult ?? string.Empty, finishReason);
                            }
                        }
                    }
                    
                    if (responseString.Contains("503") || responseString.Contains("UNAVAILABLE") || responseString.Contains("429") || responseString.Contains("exhausted"))
                    {
                        throw new Exception($"High Demand or Limit: {responseString}");
                    }

                    throw new Exception($"Gemini API error: {responseString}");
                }
                catch (Exception ex) when (ex.Message.Contains("High Demand") || ex.Message.Contains("Limit") || ex.Message.Contains("503") || ex.Message.Contains("429"))
                {
                    lastException = ex;
                    _logger.LogWarning("Gemini API call hit limit/demand on try {Try}. Error: {Error}", tryCount + 1, ex.Message);
                    if (tryCount < maxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * (tryCount + 1)), ct);
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning("Antigravity Proxy ({Model}) erişilemedi ({Message}).", geminiModel, ex.Message);
                    break;
                }
            }
        }

        // 3. Groq Cloud LLM (Yalnızca arayüzden veya ortam değişkeninden girilmiş bir Groq anahtarı varsa kullanılır)
        var groqKey = settings != null && !string.IsNullOrWhiteSpace(settings.GroqApiKey)
            ? settings.GroqApiKey
            : (_configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"]);

        if (!string.IsNullOrWhiteSpace(groqKey))
        {
            var preferredGroqModel = settings != null && !string.IsNullOrWhiteSpace(settings.GroqModel) ? settings.GroqModel : "openai/gpt-oss-120b";
            var candidateModels = new[] { preferredGroqModel, "openai/gpt-oss-20b", "qwen/qwen3.8-27b", "openai/gpt-oss-120b" }
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            int[] maxCharsPasses = { 9500, 7200, 5800 };
            for (int pass = 0; pass < maxCharsPasses.Length; pass++)
            {
                if (pass > 0)
                {
                    int waitSeconds = pass == 1 ? 14 : 20;
                    _logger.LogInformation("Groq TPM penceresinin yenilenmesi için {Wait} saniye beklenip tekrar deneniyor (Pass {Pass})...", waitSeconds, pass + 1);
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct);
                }

                var groqPrompt = CompactPromptForGroq(prompt, maxCharsPasses[pass]);
                foreach (var groqModel in candidateModels)
                {
                    try
                    {
                        _logger.LogInformation("Groq Cloud LLM ({Model}) ile sentez çağrısı yapılıyor (Prompt: {Len} krk)...", groqModel, groqPrompt.Length);
                        using var groqClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(3) };
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
                            System.Text.Json.JsonSerializer.Serialize(groqBody),
                            System.Text.Encoding.UTF8,
                            "application/json");

                        var groqResp = await groqClient.SendAsync(groqReq, ct);
                        var groqStr = await groqResp.Content.ReadAsStringAsync(ct);
                        if (groqResp.IsSuccessStatusCode)
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(groqStr);
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
                            _logger.LogWarning("Groq LLM ({Model}) returned {Status}: {Body}", groqModel, groqResp.StatusCode, groqStr);
                        }
                    }
                    catch (Exception gEx)
                    {
                        _logger.LogWarning(gEx, "Groq LLM ({Model}) sentez çağrısı başarısız oldu.", groqModel);
                    }
                }
            }
        }

        var openAiKey = _configuration["OPENAI_API_KEY"] ?? _configuration["OpenAI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(openAiKey))
        {
            var openAiModel = _configuration["OPENAI_MODEL"] ?? "gpt-4o";
            var chatClient = new ChatClient(openAiModel, openAiKey);
            var chatMessages = new List<ChatMessage> { new UserChatMessage(prompt) };
            var chatResponse = await chatClient.CompleteChatAsync(chatMessages, cancellationToken: ct);
            return (chatResponse.Value.Content[0].Text ?? string.Empty, "stop");
        }

        if (_settingsService != null)
        {
            throw new InvalidOperationException("Yapay zeka sağlayıcısından yanıt alınamadı. Lütfen sağ üstteki 'API Ayarları' menüsünden girdiğiniz API anahtarını ve model seçimini kontrol edin.");
        }

        _logger.LogWarning("No AI API key (ANTHROPIC_API_KEY, GEMINI_API_KEY, OPENAI_API_KEY) configured. Returning mock response.");
        return ("Mock Claude Response", "stop");
    }

    private static string CompactPromptForGroq(string prompt, int maxChars)
    {
        if (string.IsNullOrEmpty(prompt) || prompt.Length <= maxChars)
            return prompt;

        // Eğer bu bir Revizyon çağrısıysa (MEVCUT DOKÜMAN içeriyorsa), mevcut dokümanı ve revizyon talimatını ASLA kesme;
        // yalnızca KAYNAK BAĞLAMI kısmını kısalt:
        int refIdx = prompt.IndexOf("KAYNAK BAĞLAMI (Referans gerekirse):", StringComparison.Ordinal);
        int rulesIdx = prompt.IndexOf("ÇOK KRİTİK VE KESİN KURALLAR:", refIdx > 0 ? refIdx : 0, StringComparison.Ordinal);
        if (refIdx > 0 && rulesIdx > refIdx)
        {
            string beforeRef = prompt.Substring(0, refIdx + "KAYNAK BAĞLAMI (Referans gerekirse):".Length);
            string afterRef = prompt.Substring(rulesIdx);
            int availForRef = Math.Max(800, maxChars - beforeRef.Length - afterRef.Length);
            string refBody = prompt.Substring(beforeRef.Length, rulesIdx - beforeRef.Length);
            if (refBody.Length > availForRef)
            {
                refBody = "\n" + refBody.Substring(0, availForRef) + "\n[... Kaynak bağlamı kısaltıldı ...]\n\n";
            }
            return beforeRef + refBody + afterRef;
        }

        // Eğer prompt içinde birden fazla [KAYNAK #...] bloğu varsa, ortadaki bir kaynağı tamamen silmemek için
        // her kaynağın başından (Damıtılmış Özet + Transkript başı) eşit pay ayırarak sıkıştır.
        int firstSourceIdx = prompt.IndexOf("[KAYNAK #", StringComparison.Ordinal);
        int lastSourceIdx = prompt.LastIndexOf("[KAYNAK #", StringComparison.Ordinal);
        int lastSepIdx = lastSourceIdx >= 0 ? prompt.IndexOf("\n---", lastSourceIdx, StringComparison.Ordinal) : -1;

        if (firstSourceIdx >= 0 && lastSepIdx > firstSourceIdx)
        {
            int sourcesEndIdx = lastSepIdx + 4;
            string prefix = prompt.Substring(0, firstSourceIdx);
            string sourcesSection = prompt.Substring(firstSourceIdx, sourcesEndIdx - firstSourceIdx);
            string suffix = prompt.Substring(sourcesEndIdx);

            int maxPrefix = (int)(maxChars * 0.25);
            if (prefix.Length > maxPrefix)
            {
                prefix = prefix.Substring(0, maxPrefix - 120) + "\n[...]\n" + prefix.Substring(prefix.Length - 120);
            }

            int maxSuffix = Math.Max(3800, (int)(maxChars * 0.42));
            if (suffix.Length > maxSuffix)
            {
                // Eğer suffix başında "--- KONU VE BAŞLIK DÖKÜMÜ ---" varsa hem başını hem sonundaki kuralları koru
                int halfSuffix = maxSuffix / 2;
                suffix = suffix.Substring(0, halfSuffix) + "\n[...]\n" + suffix.Substring(suffix.Length - (maxSuffix - halfSuffix));
            }

            int remainingForSources = Math.Max(1800, maxChars - prefix.Length - suffix.Length);
            var rawBlocks = sourcesSection.Split(new[] { "[KAYNAK #" }, StringSplitOptions.RemoveEmptyEntries);
            if (rawBlocks.Length > 0)
            {
                int perSourceBudget = Math.Max(350, remainingForSources / rawBlocks.Length);
                var sb = new System.Text.StringBuilder(prefix);
                foreach (var raw in rawBlocks)
                {
                    var block = "[KAYNAK #" + raw;
                    if (block.Length > perSourceBudget)
                    {
                        block = block.Substring(0, perSourceBudget) + "\n[... Kaynak metninin devamı kısaltıldı ...]\n---\n";
                    }
                    sb.Append(block);
                }
                sb.Append(suffix);
                return sb.ToString();
            }
        }

        // Fallback: Baş ve sonu koru
        int headChars = (int)(maxChars * 0.55);
        int tailChars = maxChars - headChars;

        return prompt.Substring(0, headChars)
             + "\n\n[... Uzun kaynak metinleri token limiti nedeniyle kısaltıldı ...]\n\n"
             + prompt.Substring(prompt.Length - tailChars);
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
        return NormalizeMarkdownOutput(raw, isVideoPlan: true);
    }

    public async Task<string> ExtractClaimsAsync(string text, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.ClaimExtractionPrompt, text);
        return await CallLlmAsync(prompt, ct);
    }

    public async Task<string> VerifyClaimAsync(string claim, string contextData, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.QcVerificationPrompt, claim, contextData);
        return await CallLlmAsync(prompt, ct);
    }

    public async Task<string> BatchQualityCheckAsync(string summaryText, string contextData, int claimCount = 8, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.BatchQcPrompt, claimCount, summaryText, contextData);
        return await CallLlmAsync(prompt, ct);
    }

    public async Task<string> ReviseContentAsync(string originalContent, string revisionInstruction, string contentType, string topic, string contextData = "", CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.RevisionPrompt, contentType, originalContent, revisionInstruction, topic, contextData);
        var raw = await CallLlmAsync(prompt, ct);
        bool isPlan = contentType.Contains("Plan", StringComparison.OrdinalIgnoreCase);
        return NormalizeMarkdownOutput(raw, isVideoPlan: isPlan);
    }

    public async Task<string> ReQualityCheckAsync(string currentContent, string previousQcReportJson, string contextData, int claimCount = 8, CancellationToken ct = default)
    {
        var prevReport = string.IsNullOrWhiteSpace(previousQcReportJson) ? "Önceki QC raporu bulunmuyor (İlk denetim)." : previousQcReportJson;
        var prompt = string.Format(PromptTemplates.ReQcVerificationPrompt, claimCount, currentContent, contextData, prevReport);
        return await CallLlmAsync(prompt, ct);
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

    public async Task<string> GenerateSeriesVideoContentAsync(string bolumBasligi, string bolumKonulariJson, string oncekiVideoDevirNotuJson, string targetAudience, string contextData, string userInstructions = "", CancellationToken ct = default)
    {
        var enrichedContext = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(bolumKonulariJson))
        {
            enrichedContext.AppendLine($"[BU BÖLÜMDE İŞLENECEK ALT KONULAR]: {bolumKonulariJson}");
        }
        if (!string.IsNullOrWhiteSpace(oncekiVideoDevirNotuJson))
        {
            enrichedContext.AppendLine($"[ÖNCEKİ BÖLÜMDEN DEVİR NOTU]: {oncekiVideoDevirNotuJson}");
        }
        if (!string.IsNullOrWhiteSpace(userInstructions))
        {
            enrichedContext.AppendLine($"[KULLANICI ÖZEL DİREKTİFİ]: {userInstructions}");
        }
        enrichedContext.AppendLine(contextData);

        var fullContext = enrichedContext.ToString();

        // Hızlı Mod ile birebir aynı araştırma özeti ve video planı formatını üretmek için
        // aynı iki üretim fonksiyonunu çağırıp standart JSON olarak paketliyoruz:
        var arastirmaOzeti = await GenerateResearchSummaryAsync(bolumBasligi, "Uzun ve Detaylı (10-15 Dakika)", targetAudience, fullContext, ct);
        var videoPlani = await GenerateVideoPlanAsync(bolumBasligi, "10-15 Dakika", fullContext, ct);

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            ArastirmaOzeti = arastirmaOzeti,
            VideoPlani = videoPlani,
            DevirNotu = $"{bolumBasligi} işlendi."
        });
    }

    public async Task<string> ExtractSourceTopicsAsync(string kaynakAdi, string metin, int parcaNo, int parcaSayisi, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.SourceTopicExtractionPrompt, kaynakAdi, parcaNo, parcaSayisi, metin);
        return await CallLlmAsync(prompt, ct);
    }
}
