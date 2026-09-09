namespace VideoOzet.Business.Events;

public class SendNotificationEvent
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ActionUrl { get; set; }
    public bool SendEmail { get; set; } = true;
    public bool SendSms { get; set; } = false;
    public bool SendPush { get; set; } = true;
    // E-posta/SMS/Push için kullanıcı bilgisi (DB'ye gitmemek için)
    public string? UserEmail { get; set; }
    public string? UserPhone { get; set; }
    public string? FcmToken { get; set; }
    /// <summary>Duplicate önleme — aynı key ile ikinci kez bildirim yazılmaz</summary>
    public string? IdempotencyKey { get; set; }
}
