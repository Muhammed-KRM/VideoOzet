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

    public ClaudeSynthesisProvider(ILogger<ClaudeSynthesisProvider> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// CallLlmAsync ile aynı sağlayıcı seçim sırasını kullanır (Anthropic → Gemini → OpenAI).
    /// </summary>
    public string ActiveModelName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_configuration["ANTHROPIC_API_KEY"]))
                return _configuration["CLAUDE_MODEL"] ?? "claude-3-5-sonnet-20240620";

            if (!string.IsNullOrWhiteSpace(_configuration["GEMINI_API_KEY"]))
                return _configuration["GEMINI_MODEL"] ?? "gemini-1.5-flash";

            if (!string.IsNullOrWhiteSpace(_configuration["OPENAI_API_KEY"] ?? _configuration["OpenAI:ApiKey"]))
                return _configuration["OPENAI_MODEL"] ?? "gpt-4o";

            return "mock";
        }
    }

    private async Task<string> CallLlmAsync(string prompt, CancellationToken ct)
    {
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
                MaxTokens = 4096,
                Model = model,
                Temperature = 0.2m // Düşük sıcaklık, tutarlı ve halüsinasyonsuz çıktılar için
            };

            var response = await client.Messages.GetClaudeMessageAsync(parameters, ct);
            return (response.Content[0] as TextContent)?.Text ?? string.Empty;
        }

        var geminiKey = _configuration["GEMINI_API_KEY"];
        if (!string.IsNullOrWhiteSpace(geminiKey))
        {
            var geminiModel = _configuration["GEMINI_MODEL"] ?? "gemini-1.5-flash";
            var apiKeys = geminiKey.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(k => k.Trim())
                                   .ToArray();
            
            Exception lastException = null;
            int maxRetries = Math.Max(apiKeys.Length, 3);

            for (int tryCount = 0; tryCount < maxRetries; tryCount++)
            {
                var keyToUse = apiKeys[tryCount % apiKeys.Length];

                try
                {
                    using var httpClient = new System.Net.Http.HttpClient();
                    var requestBody = new
                    {
                        model = geminiModel,
                        messages = new[]
                        {
                            new { role = "user", content = prompt }
                        }
                    };
                    var jsonContent = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
                    var requestUrl = "http://localhost:8045/v1/chat/completions";
                    
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
                            if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                            {
                                var textResult = content.GetString();
                                return textResult ?? string.Empty;
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
                    _logger.LogError(ex, "Gemini modeli {Model} beklenmedik hata ile başarısız oldu", geminiModel);
                    throw new Exception($"[GEMINI_API_ERROR]: Beklenmeyen bir hata oluştu: {ex.Message}", ex);
                }
            }

            _logger.LogError(lastException, "Gemini modeli {Model} tüm denemelere rağmen başarısız oldu", geminiModel);
            throw new Exception("[GEMINI_API_ERROR]: Servis şu anda yoğun talep altında. Lütfen kısa bir süre sonra tekrar deneyin.", lastException);
        }

        var openAiKey = _configuration["OPENAI_API_KEY"] ?? _configuration["OpenAI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(openAiKey))
        {
            var openAiModel = _configuration["OPENAI_MODEL"] ?? "gpt-4o";
            var chatClient = new ChatClient(openAiModel, openAiKey);
            var chatMessages = new List<ChatMessage> { new UserChatMessage(prompt) };
            var chatResponse = await chatClient.CompleteChatAsync(chatMessages, cancellationToken: ct);
            return chatResponse.Value.Content[0].Text ?? string.Empty;
        }

        _logger.LogWarning("No AI API key (ANTHROPIC_API_KEY, GEMINI_API_KEY, OPENAI_API_KEY) configured. Returning mock response.");
        return "Mock Claude Response";
    }

    public async Task<string> GenerateResearchSummaryAsync(string topic, string targetLength, string targetAudience, string contextData, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.SynthesisResearchPrompt, topic, targetLength, targetAudience, contextData);
        return await CallLlmAsync(prompt, ct);
    }

    public async Task<string> GenerateVideoPlanAsync(string topic, string targetLength, string contextData, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.SynthesisVideoPlanPrompt, topic, targetLength, contextData);
        return await CallLlmAsync(prompt, ct);
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
        return await CallLlmAsync(prompt, ct);
    }

    public async Task<string> ReQualityCheckAsync(string currentContent, string previousQcReportJson, string contextData, int claimCount = 8, CancellationToken ct = default)
    {
        var prevReport = string.IsNullOrWhiteSpace(previousQcReportJson) ? "Önceki QC raporu bulunmuyor (İlk denetim)." : previousQcReportJson;
        var prompt = string.Format(PromptTemplates.ReQcVerificationPrompt, claimCount, currentContent, contextData, prevReport);
        return await CallLlmAsync(prompt, ct);
    }
}
