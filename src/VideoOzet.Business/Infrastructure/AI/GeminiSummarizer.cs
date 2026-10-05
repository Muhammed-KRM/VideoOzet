using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class GeminiSummarizer : IGeminiProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiSummarizer> _logger;
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    private static int _currentKeyIndex = 0;

    // Proxy'ye gönderilen model; kayıtlardaki LlmModel alanı da buradan beslenir.
    private const string ModelName = "gemini-3.8-flash-tiered";

    public string ActiveModelName => ModelName;

    public GeminiSummarizer(IConfiguration configuration, ILogger<GeminiSummarizer> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> SummarizeAsync(string transcript, string modelName = "gemini-1.5-flash")
    {
        var apiKeyStr = _configuration["GEMINI_API_KEY"];
        if (string.IsNullOrEmpty(apiKeyStr))
        {
            _logger.LogWarning("GEMINI_API_KEY is not set. Using mock summary for development.");
            return "{\"ozetMetni\":\"Mock Özet\", \"konuBasliklari\":[\"Mock Baslik\"], \"konuEtiketleri\":[\"mock\", \"test\"]}";
        }

        var apiKeys = apiKeyStr.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(k => k.Trim())
                               .ToArray();

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

        var requestBody = new
        {
            model = ModelName,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        Exception? lastException = null;

        for (int tryCount = 0; tryCount < apiKeys.Length; tryCount++)
        {
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var keyToUse = apiKeys[_currentKeyIndex % apiKeys.Length];
            try
            {
                var requestUrl = "http://localhost:8045/v1/chat/completions";
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                requestMessage.Headers.Add("Authorization", $"Bearer {keyToUse}");
                requestMessage.Content = jsonContent;
                
                var response = await _httpClient.SendAsync(requestMessage);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (responseString.Contains("429") || responseString.Contains("quota") || responseString.Contains("exhausted"))
                    {
                        throw new Exception($"Quota Exceeded: {responseString}");
                    }
                    else if (responseString.Contains("503") || responseString.Contains("UNAVAILABLE") || responseString.Contains("high demand"))
                    {
                        throw new Exception($"High Demand: {responseString}");
                    }
                    else
                    {
                        throw new Exception($"API error: {response.StatusCode} - {responseString}");
                    }
                }

                using var jsonDoc = JsonDocument.Parse(responseString);
                var root = jsonDoc.RootElement;
                
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }

                throw new Exception($"Unexpected API response format: {responseString}");
            }
            catch (Exception ex) when (ex.Message.Contains("Quota Exceeded") || ex.Message.Contains("High Demand") || ex.Message.Contains("exhausted"))
            {
                _logger.LogWarning("API Key {Key} hit limit or high demand on proxy. Rotating... Error: {Error}", keyToUse.Substring(0, Math.Min(keyToUse.Length, 5)) + "...", ex.Message);
                System.Threading.Interlocked.Increment(ref _currentKeyIndex);
                lastException = ex;
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            catch
            {
                throw;
            }
        }

        throw new Exception("Bütün yedek API anahtarlarının limiti dolmuş durumda veya kullanılamıyor! Lütfen yeni bir API anahtarı ekleyin.", lastException);
    }
}
