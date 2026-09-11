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

    public async Task<string> SummarizeAsync(string transcript, string modelName = "gemini-3.5-flash")
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
        Sen bir bilgi çıkarma ve rafinasyon uzmanısın. Görevin aşağıdaki metni klasik anlamda 'özetlemek' değil, 'damıtmak'tır. Aşağıdaki kurallara kesinlikle uy: 
        1) Konuyla ilgili verilen TÜM bilgileri, iddiaları, kuralları ve teknik detayları %100 oranında koru. Hiçbir bilgi kırıntısını atlama. 
        2) Konuşmacının kendini tekrar ettiği yerleri, konudan tamamen bağımsız anılarını/sohbetlerini ve 'ııı, eee, yani' gibi boş laflarını tamamen temizle. 
        3) Çıktın, asıl metnin bilgi yoğunluğunu kaybetmeden sadece gereksiz tekrarlardan ve konu dışı gürültüden arındırılmış, saf ve akıcı bir bilgi dökümü olmalıdır.

        Lütfen cevabı sadece geçerli bir JSON formatında döndür.
        
        İstenen JSON formatı:
        {{
            ""ozetMetni"": ""Damıtılmış, eksiksiz ancak gereksiz tekrarlardan arındırılmış tam metin"",
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
