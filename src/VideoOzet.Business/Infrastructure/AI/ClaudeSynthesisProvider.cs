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
            var geminiModel = _configuration["GEMINI_MODEL"] ?? "gemini-flash-latest";
            var googleAI = new GoogleAI(geminiKey);
            var genModel = googleAI.GenerativeModel(model: geminiModel);
            var response = await genModel.GenerateContent(prompt);
            return response.Text ?? string.Empty;
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
}
