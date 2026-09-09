using Microsoft.Extensions.AI;
using OllamaSharp;

namespace VideoOzet.Worker.Services;

public class OllamaService
{
    private readonly OllamaApiClient _client;
    private readonly ILogger<OllamaService> _logger;
    private const string Model = "phi3:mini";

    // Few-shot prompt — eğitim yok, sadece örnekler
    private const string SystemPrompt = """
        Sen bir içerik moderatörüsün. Türkçe ilan metinlerinde telefon numarası,
        e-posta adresi veya harici link olup olmadığını tespit ediyorsun.
        Sadece JSON formatında yanıt ver, başka hiçbir şey yazma.

        Örnekler:
        Metin: "Matematik dersi veriyorum, 10 yıl deneyimim var"
        Yanıt: {"violation": false}

        Metin: "0532 123 45 67 numaralı telefonu arayın"
        Yanıt: {"violation": true, "type": "phone"}

        Metin: "sıfır beş üç iki bir iki üç dört beş altı yedi"
        Yanıt: {"violation": true, "type": "phone"}

        Metin: "bilgi@gmail.com adresine yazın"
        Yanıt: {"violation": true, "type": "email"}

        Metin: "www.sitem.com adresimi ziyaret edin"
        Yanıt: {"violation": true, "type": "link"}

        Metin: "s-ı-f-ı-r b-e-ş üç iki..."
        Yanıt: {"violation": true, "type": "phone"}
        """;

    public OllamaService(IConfiguration config, ILogger<OllamaService> logger)
    {
        var ollamaUrl = config["Ollama:BaseUrl"] ?? "http://ollama:11434";
        _client = new OllamaApiClient(new Uri(ollamaUrl));
        _client.SelectedModel = Model;
        _logger = logger;
    }

    public async Task<OllamaModerationResult> AnalyzeAsync(string title, string description,
        CancellationToken ct = default)
    {
        try
        {
            var userMessage = $"Metin: \"{title} {description}\"";

            // OllamaSharp 5.x — IChatClient interface üzerinden chat
            IChatClient chatClient = _client;

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, userMessage)
            };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var json = response.Text?.Trim() ?? "{}";

            // JSON parse
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var violation = root.TryGetProperty("violation", out var v) && v.GetBoolean();
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

            return new OllamaModerationResult(violation, type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ollama analizi başarısız, temiz kabul ediliyor");
            // Ollama hata verirse ilanı engelleme — false negative tercih edilir
            return new OllamaModerationResult(false, null);
        }
    }
}

public record OllamaModerationResult(bool IsViolation, string? ViolationType);
