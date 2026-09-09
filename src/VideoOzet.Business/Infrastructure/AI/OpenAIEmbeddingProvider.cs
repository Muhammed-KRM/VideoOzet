using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenAI.Embeddings;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class OpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIEmbeddingProvider> _logger;

    public OpenAIEmbeddingProvider(IConfiguration configuration, ILogger<OpenAIEmbeddingProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, string modelName = "text-embedding-3-small")
    {
        var embeddings = await GenerateEmbeddingsAsync(new List<string> { text }, modelName);
        return embeddings.FirstOrDefault();
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, string modelName = "text-embedding-3-small")
    {
        var apiKey = _configuration["OPENAI_API_KEY"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("OPENAI_API_KEY is not set. Generating mock embeddings of size 1536.");
            // pgvector için varsayılan model boyutu 1536'dır.
            return texts.Select(_ => new float[1536]).ToList();
        }

        var client = new EmbeddingClient(modelName, apiKey);
        
        var options = new EmbeddingGenerationOptions
        {
            Dimensions = 1536
        };

        var response = await client.GenerateEmbeddingsAsync(texts, options);

        var result = new List<float[]>();
        foreach (var embedding in response.Value)
        {
            result.Add(embedding.ToFloats().ToArray());
        }

        return result;
    }
}
