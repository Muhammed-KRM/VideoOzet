using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.Sms;

public class NetgsmSmsService : ISmsService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;
    private readonly ILogger<NetgsmSmsService> _logger;

    public NetgsmSmsService(IConfiguration config, IHttpClientFactory httpFactory, ILogger<NetgsmSmsService> logger)
    {
        _config = config;
        _http = httpFactory.CreateClient("Netgsm");
        _logger = logger;
    }

    public async Task SendAsync(string phoneNumber, string message)
    {
        var isEnabled = _config.GetValue<bool>("Netgsm:Enabled", false);
        if (!isEnabled)
        {
            _logger.LogInformation("SMS gönderimi devre dışı. Telefon: {Phone}, Mesaj: {Message}", phoneNumber, message);
            return;
        }

        var user = _config["Netgsm:Username"];
        var pass = _config["Netgsm:Password"];
        var header = _config["Netgsm:Header"] ?? "VideoOzet";

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            _logger.LogWarning("Netgsm kullanıcı adı veya şifre eksik. SMS gönderilemedi.");
            return;
        }

        try
        {
            // Telefon numarasını temizle (sadece rakamlar)
            var cleanPhone = new string(phoneNumber.Where(char.IsDigit).ToArray());
            
            // Türkiye formatına çevir (90 ile başlamalı)
            if (cleanPhone.StartsWith("0"))
                cleanPhone = "90" + cleanPhone[1..];
            else if (!cleanPhone.StartsWith("90"))
                cleanPhone = "90" + cleanPhone;

            // Netgsm HTTP API
            var url = $"https://api.netgsm.com.tr/sms/send/get?" +
                      $"usercode={Uri.EscapeDataString(user)}&password={Uri.EscapeDataString(pass)}" +
                      $"&gsmno={cleanPhone}&message={Uri.EscapeDataString(message)}&msgheader={Uri.EscapeDataString(header)}";

            var response = await _http.GetAsync(url);
            var result = await response.Content.ReadAsStringAsync();

            if (result.StartsWith("00"))
            {
                _logger.LogInformation("SMS başarıyla gönderildi. Telefon: {Phone}", cleanPhone);
            }
            else
            {
                _logger.LogWarning("SMS gönderimi başarısız. Telefon: {Phone}, Hata: {Error}", cleanPhone, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMS gönderimi sırasında hata. Telefon: {Phone}", phoneNumber);
        }
    }
}
