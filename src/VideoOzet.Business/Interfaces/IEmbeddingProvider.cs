using System.Collections.Generic;
using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public interface IEmbeddingProvider
{
    Task<float[]> GenerateEmbeddingAsync(string text, string modelName = "text-embedding-3-small");
    Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, string modelName = "text-embedding-3-small");
}
