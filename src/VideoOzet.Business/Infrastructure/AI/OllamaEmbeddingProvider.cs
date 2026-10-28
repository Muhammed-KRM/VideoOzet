using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _ollamaUrl;
    private readonly string _defaultModel;
    private readonly bool _isStandalone;
    private readonly ILogger<OllamaEmbeddingProvider> _logger;

    public OllamaEmbeddingProvider(
        IConfiguration configuration,
        ILogger<OllamaEmbeddingProvider> logger,
        HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _ollamaUrl = configuration["OLLAMA_URL"] ?? configuration["Ollama:Url"] ?? "http://localhost:11435";
        _defaultModel = configuration["OLLAMA_EMBED_MODEL"] ?? "bge-m3";
        _isStandalone = string.Equals(configuration["StandaloneMode"], "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, string modelName = "bge-m3")
    {
        var model = string.IsNullOrEmpty(modelName) || modelName == "text-embedding-3-small" ? _defaultModel : modelName;
        var list = await GenerateEmbeddingsAsync(new List<string> { text }, model);
        return list.FirstOrDefault() ?? Array.Empty<float>();
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, string modelName = "bge-m3")
    {
        if (texts == null || !texts.Any())
            return new List<float[]>();

        if (_isStandalone)
            return texts.Select(CreateLocalFallbackEmbedding).ToList();

        var model = string.IsNullOrEmpty(modelName) || modelName == "text-embedding-3-small" ? _defaultModel : modelName;

        try
        {
            var url = $"{_ollamaUrl.TrimEnd('/')}/api/embed";
            var payload = new
            {
                model = model,
                input = texts
            };

            var response = await _httpClient.PostAsJsonAsync(url, payload);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            if (doc.RootElement.TryGetProperty("embeddings", out var embeddingsProp) && embeddingsProp.ValueKind == JsonValueKind.Array)
            {
                var result = new List<float[]>();
                foreach (var embElem in embeddingsProp.EnumerateArray())
                {
                    var vec = new float[embElem.GetArrayLength()];
                    int i = 0;
                    foreach (var val in embElem.EnumerateArray())
                    {
                        vec[i++] = val.GetSingle();
                    }
                    result.Add(vec);
                }
                return result;
            }

            _logger.LogWarning("Ollama embed yanıtında 'embeddings' bulunamadı.");
            return texts.Select(_ => new float[1024]).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama servisine ulaşılamadı (Model: {Model}), yerel metin vektör yedeği kullanılıyor: {Message}", model, ex.Message);
            return texts.Select(CreateLocalFallbackEmbedding).ToList();
        }
    }

    private static float[] CreateLocalFallbackEmbedding(string text)
    {
        var vec = new float[1024];
        if (string.IsNullOrWhiteSpace(text)) return vec;

        var tokens = text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            int hash = Math.Abs(token.GetHashCode());
            vec[hash % 1024] += 1f;
        }

        double norm = Math.Sqrt(vec.Sum(v => (double)v * v));
        if (norm > 0)
        {
            for (int i = 0; i < vec.Length; i++)
                vec[i] = (float)(vec[i] / norm);
        }
        return vec;
    }
}
