using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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

    private async Task<string> CallClaudeAsync(string prompt, CancellationToken ct)
    {
        var apiKey = _configuration["ANTHROPIC_API_KEY"];
        var model = _configuration["CLAUDE_MODEL"] ?? "claude-3-5-sonnet-20240620";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("ANTHROPIC_API_KEY is missing. Returning mock response.");
            return "Mock Claude Response";
        }

        var client = new AnthropicClient(apiKey);
        var messages = new List<Message>
        {
            new Message(RoleType.User, prompt)
        };

        var parameters = new MessageParameters
        {
            Messages = messages,
            MaxTokens = 4096,
            Model = model,
            Temperature = 0.2m // Düşük sıcaklık, tutarlı ve halüsinasyonsuz çıktılar için (özellikle RAG ve QC)
        };

        var response = await client.Messages.GetClaudeMessageAsync(parameters, ct);
        return (response.Content[0] as TextContent)?.Text ?? string.Empty;
    }

    public async Task<string> GenerateResearchSummaryAsync(string topic, string targetLength, string targetAudience, string contextData, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.SynthesisResearchPrompt, topic, targetLength, targetAudience, contextData);
        return await CallClaudeAsync(prompt, ct);
    }

    public async Task<string> GenerateVideoPlanAsync(string topic, string targetLength, string contextData, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.SynthesisVideoPlanPrompt, topic, targetLength, contextData);
        return await CallClaudeAsync(prompt, ct);
    }

    public async Task<string> ExtractClaimsAsync(string text, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.ClaimExtractionPrompt, text);
        return await CallClaudeAsync(prompt, ct);
    }

    public async Task<string> VerifyClaimAsync(string claim, string contextData, CancellationToken ct = default)
    {
        var prompt = string.Format(PromptTemplates.QcVerificationPrompt, claim, contextData);
        return await CallClaudeAsync(prompt, ct);
    }
}
