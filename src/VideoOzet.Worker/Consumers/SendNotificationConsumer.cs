using FirebaseAdmin.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Worker.Consumers;

public class SendNotificationConsumer : IConsumer<SendNotificationEvent>
{
    private readonly ILogger<SendNotificationConsumer> _logger;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;

    public SendNotificationConsumer(
        ILogger<SendNotificationConsumer> logger,
        INotificationService notificationService,
        IEmailService emailService,
        ISmsService smsService)
    {
        _logger = logger;
        _notificationService = notificationService;
        _emailService = emailService;
        _smsService = smsService;
    }

    public async Task Consume(ConsumeContext<SendNotificationEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("SendNotificationEvent alındı: {UserId} - {Type}", msg.UserId, msg.Type);

        try
        {
            // 1. Site içi bildirim (her zaman)
            await _notificationService.CreateAsync(
                msg.UserId, msg.Type, msg.Title, msg.Message, msg.ActionUrl, msg.IdempotencyKey);

            // 2. E-posta (kullanıcı izin verdiyse)
            if (msg.SendEmail && !string.IsNullOrEmpty(msg.UserEmail))
            {
                await _emailService.SendEmailAsync(msg.UserEmail, msg.Title,
                    $"<p>{msg.Message}</p>");
            }

            // 3. SMS (kullanıcı izin verdiyse)
            if (msg.SendSms && !string.IsNullOrEmpty(msg.UserPhone))
            {
                await _smsService.SendAsync(msg.UserPhone, msg.Message);
            }

            // 4. FCM Push (mobil)
            if (msg.SendPush && !string.IsNullOrEmpty(msg.FcmToken))
            {
                try
                {
                    await FirebaseMessaging.DefaultInstance.SendAsync(new Message
                    {
                        Token = msg.FcmToken,
                        Notification = new FirebaseAdmin.Messaging.Notification
                        {
                            Title = msg.Title,
                            Body = msg.Message
                        },
                        Data = new Dictionary<string, string>
                        {
                            ["type"] = msg.Type,
                            ["actionUrl"] = msg.ActionUrl ?? ""
                        }
                    });
                }
                catch (Exception ex)
                {
                    // Push başarısız olsa bile diğer kanallar etkilenmesin (fail-open)
                    _logger.LogWarning(ex, "FCM push gönderilemedi: {UserId}", msg.UserId);
                }
            }

            _logger.LogInformation("Bildirim gönderildi: {UserId} - {Type}", msg.UserId, msg.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bildirim gönderilemedi: {UserId}", msg.UserId);
            throw; // MassTransit retry
        }
    }
}
