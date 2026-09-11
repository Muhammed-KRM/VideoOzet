using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public interface IGeminiProvider
{
    Task<string> SummarizeAsync(string transcript, string modelName = "gemini-flash-latest");
}
