using Mscc.GenerativeAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class GeminiSummarizer : IGeminiProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiSummarizer> _logger;

    public GeminiSummarizer(IConfiguration configuration, ILogger<GeminiSummarizer> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> SummarizeAsync(string transcript, string modelName = "gemini-2.0-flash")
    {
        var apiKey = _configuration["GEMINI_API_KEY"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("GEMINI_API_KEY is not set. Using mock summary for development.");
            return "{\"ozetMetni\":\"Mock Özet\", \"konuBasliklari\":[\"Mock Baslik\"], \"konuEtiketleri\":[\"mock\", \"test\"]}";
        }

        var googleAI = new GoogleAI(apiKey);
        var model = googleAI.GenerativeModel(model: modelName);

        var prompt = $@"
        Aşağıdaki video transkriptini inceleyip yapılandırılmış bir özet üret. 
        Lütfen cevabı sadece geçerli bir JSON formatında döndür.
        
        İstenen JSON formatı:
        {{
            ""ozetMetni"": ""Eğitmen tarzında detaylı özet"",
            ""konuBasliklari"": [""Başlık 1"", ""Başlık 2""],
            ""konuEtiketleri"": [""etiket1"", ""etiket2""]
        }}

        Transkript:
        {transcript}
        ";

        var response = await model.GenerateContent(prompt);
        return response.Text;
    }
}
