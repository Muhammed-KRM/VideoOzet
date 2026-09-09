using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.Email;

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;

    public SmtpEmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        var smtpSettings = _config.GetSection("SmtpSettings");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            smtpSettings["SenderName"] ?? "VideoOzet.com",
            smtpSettings["SenderEmail"] ?? "noreply@ozeders.com"));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            smtpSettings["Host"] ?? "smtp-relay.brevo.com",
            int.Parse(smtpSettings["Port"] ?? "587"),
            SecureSocketOptions.StartTls);

        await client.AuthenticateAsync(
            smtpSettings["Username"] ?? "",
            smtpSettings["Password"] ?? "");

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    public async Task SendTemplatedEmailAsync(string to, string templateName, Dictionary<string, string> replacements)
    {
        // Basit template sistemi: şablon adına göre HTML oluştur
        string subject;
        string htmlBody;

        switch (templateName)
        {
            case "WelcomeEmail":
                subject = "VideoOzet.com'a Hoş Geldiniz! 🎓";
                htmlBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                        <h1 style='color: #6366f1;'>Hoş Geldiniz, {replacements.GetValueOrDefault("FullName", "")}!</h1>
                        <p>VideoOzet.com'a başarıyla kayıt oldunuz.</p>
                        <p>Artık binlerce öğretmen ve öğrenci arasında aradığınız dersi bulabilir veya ders verebilirsiniz.</p>
                        <a href='https://VideoOzet.com/panel' style='display:inline-block; padding:12px 24px; background:#6366f1; color:white; border-radius:8px; text-decoration:none;'>Panelime Git</a>
                    </div>";
                break;

            case "ListingApproved":
                subject = "İlanınız Onaylandı! ✅";
                htmlBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                        <h1 style='color: #22c55e;'>İlanınız Yayında!</h1>
                        <p><strong>{replacements.GetValueOrDefault("ListingTitle", "")}</strong> başlıklı ilanınız başarıyla onaylandı ve artık yayında.</p>
                    </div>";
                break;

            default:
                subject = "VideoOzet.com Bilgilendirme";
                htmlBody = "<p>Bu bir bilgilendirme mesajıdır.</p>";
                break;
        }

        await SendEmailAsync(to, subject, htmlBody);
    }
}
