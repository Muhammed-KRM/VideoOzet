using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public interface IGeminiProvider
{
    /// <summary>
    /// Özetleme isteğinde fiilen kullanılan model adı (kayıtlardaki LlmModel alanı için).
    /// </summary>
    string ActiveModelName => string.Empty;

    Task<string> SummarizeAsync(string transcript, string modelName = "gemini-1.5-flash");
}
