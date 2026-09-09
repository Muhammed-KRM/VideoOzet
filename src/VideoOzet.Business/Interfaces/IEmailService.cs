namespace VideoOzet.Business.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
    Task SendTemplatedEmailAsync(string to, string templateName, Dictionary<string, string> replacements);
}
