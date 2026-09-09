using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace VideoOzet.Business.Infrastructure.Messaging;

public interface IFcmService
{
    Task SendNotificationAsync(string token, string title, string body, Dictionary<string, string>? data = null);
    Task SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null);
}

public class FcmService : IFcmService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<FcmService> _logger;

    public FcmService(HttpClient httpClient, IConfiguration config, ILogger<FcmService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task SendNotificationAsync(string token, string title, string body, Dictionary<string, string>? data = null)
    {
        var isEnabled = _config.GetValue<bool>("Firebase:Enabled", false);
        if (!isEnabled)
        {
            _logger.LogInformation("FCM devre dışı. Token: {Token}, Başlık: {Title}", token, title);
            return;
        }

        var serverKey = _config["Firebase:ServerKey"];
        if (string.IsNullOrEmpty(serverKey))
        {
            _logger.LogWarning("Firebase ServerKey eksik. FCM bildirimi gönderilemedi.");
            return;
        }

        try
        {
            var message = new
            {
                to = token,
                notification = new
                {
                    title = title,
                    body = body,
                    icon = "ic_notification",
                    sound = "default"
                },
                data = data ?? new Dictionary<string, string>(),
                priority = "high"
            };

            var json = JsonSerializer.Serialize(message);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"key={serverKey}");

            var response = await _httpClient.PostAsync("https://fcm.googleapis.com/fcm/send", content);
            var result = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("FCM bildirimi başarıyla gönderildi. Token: {Token}", token);
            }
            else
            {
                _logger.LogWarning("FCM bildirimi başarısız. Token: {Token}, Hata: {Error}", token, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM bildirimi gönderimi sırasında hata. Token: {Token}", token);
        }
    }

    public async Task SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null)
    {
        var isEnabled = _config.GetValue<bool>("Firebase:Enabled", false);
        if (!isEnabled)
        {
            _logger.LogInformation("FCM devre dışı. Topic: {Topic}, Başlık: {Title}", topic, title);
            return;
        }

        var serverKey = _config["Firebase:ServerKey"];
        if (string.IsNullOrEmpty(serverKey))
        {
            _logger.LogWarning("Firebase ServerKey eksik. FCM bildirimi gönderilemedi.");
            return;
        }

        try
        {
            var message = new
            {
                to = $"/topics/{topic}",
                notification = new
                {
                    title = title,
                    body = body,
                    icon = "ic_notification",
                    sound = "default"
                },
                data = data ?? new Dictionary<string, string>(),
                priority = "high"
            };

            var json = JsonSerializer.Serialize(message);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"key={serverKey}");

            var response = await _httpClient.PostAsync("https://fcm.googleapis.com/fcm/send", content);
            var result = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("FCM topic bildirimi başarıyla gönderildi. Topic: {Topic}", topic);
            }
            else
            {
                _logger.LogWarning("FCM topic bildirimi başarısız. Topic: {Topic}, Hata: {Error}", topic, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM topic bildirimi gönderimi sırasında hata. Topic: {Topic}", topic);
        }
    }
}
