using MassTransit;
using System.Text.RegularExpressions;
using VideoOzet.Business.Events;
using VideoOzet.Business.Infrastructure.Moderation;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Business.Services;

public class ModerationManager : IModerationService
{
    // ═══════════════════════════════════════════════
    // HATA KODLARI — ModerationManager (Prefix: MM)
    // ═══════════════════════════════════════════════
    private const string EC_CHECK  = "MM-001"; // CheckContent
    private const string EC_STRIKE = "MM-002"; // AddStrikeAsync
    // ═══════════════════════════════════════════════

    private readonly IRepository<User> _userRepo;
    private readonly IRepository<ViolationLog> _violationRepo;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogService _logService;

    // Compiled regex'ler — static, bir kez derlenir

    // Türk mobil: 05XX ile başlayan, tüm ayraç varyasyonları (boşluk, tire, nokta vb.)
    private static readonly Regex PhoneRegex = new(
        @"(\+90|0090|0)[\s\-\.\(\)\*\[\]\/\\]?[5][0-9][\s\-\.\(\)\*\[\]\/\\]?\d{2}[\s\-\.\(\)\*\[\]\/\\]?\d{3}[\s\-\.\(\)\*\[\]\/\\]?\d{2}[\s\-\.\(\)\*\[\]\/\\]?\d{2}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Boşluksuz 11 haneli (05XXXXXXXXX)
    private static readonly Regex PhoneSimpleRegex = new(
        @"\b0[5][0-9]\d{8}\b",
        RegexOptions.Compiled);

    // Boşluklu/ayraçlı format: 0505 050 5838, 0505-050-5838 vb.
    private static readonly Regex PhoneSpacedRegex = new(
        @"\b0[5][0-9]\d[\s\-\.\(\)]\d{3}[\s\-\.\(\)]\d{2}[\s\-\.\(\)]\d{2}\b",
        RegexOptions.Compiled);

    // 5XX ile başlayan (başta 0 yok): 505 050 5838
    private static readonly Regex PhoneNoLeadingZeroRegex = new(
        @"\b[5][0-9]\d[\s\-\.\(\)]?\d{3}[\s\-\.\(\)]?\d{2}[\s\-\.\(\)]?\d{2}\b",
        RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LinkRegex = new(
        @"(https?://[^\s]+|\bwww\.[a-zA-Z0-9\-]+\.[a-zA-Z]{2,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ModerationManager(
        IRepository<User> userRepo,
        IRepository<ViolationLog> violationRepo,
        IPublishEndpoint publishEndpoint,
        ILogService logService)
    {
        _userRepo = userRepo;
        _violationRepo = violationRepo;
        _publishEndpoint = publishEndpoint;
        _logService = logService;
    }

    public ModerationResult CheckContent(string title, string description)
    {
        // Normalize et (homoglyph + Türkçe rakam)
        var raw = $"{title} {description}";
        var normalized = TurkishTextNormalizer.Normalize(raw);

        if (PhoneRegex.IsMatch(normalized) || PhoneSimpleRegex.IsMatch(normalized)
            || PhoneSpacedRegex.IsMatch(normalized) || PhoneNoLeadingZeroRegex.IsMatch(normalized))
            return ModerationResult.Violation("Phone",
                "İlan içeriğinde telefon numarası paylaşılamaz. Platform üzerinden iletişim kurulmalıdır.");

        if (EmailRegex.IsMatch(normalized))
            return ModerationResult.Violation("Email",
                "İlan içeriğinde e-posta adresi paylaşılamaz.");

        if (LinkRegex.IsMatch(normalized))
            return ModerationResult.Violation("Link",
                "İlan içeriğinde harici link paylaşılamaz.");

        return ModerationResult.Clean();
    }

    public async Task AddStrikeAsync(Guid userId, Guid? listingId, string listingTitle,
        string violationType, string detectedContent, string detectedBy)
    {
        try
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) return;

            user.ViolationCount++;
            user.LastViolationAt = DateTime.UtcNow;

            // Ban hesapla
            var ban = CalculateBan(user.ViolationCount);
            if (ban.HasValue)
            {
                user.BannedUntil = ban == TimeSpan.MaxValue
                    ? DateTime.MaxValue
                    : DateTime.UtcNow.Add(ban.Value);
                user.BanReason = $"Otomatik: {violationType} ihlali ({user.ViolationCount}. ihlal)";
            }

            _userRepo.Update(user);

            // İhlal kaydı
            await _violationRepo.AddAsync(new ViolationLog
            {
                UserId = userId,
                ListingId = listingId,
                ListingTitle = listingTitle,
                ViolationType = violationType,
                DetectedContent = detectedContent.Length > 200
                    ? detectedContent[..200] + "..." : detectedContent,
                DetectedBy = detectedBy,
                CreatedAt = DateTime.UtcNow
            });

            await _userRepo.SaveChangesAsync();

            // Bildirim gönder — ban mı uyarı mı?
            var isBanned = ban.HasValue;
            var notifType = isBanned ? "Ban" : "Warning";
            var notifTitle = isBanned
                ? (ban == TimeSpan.MaxValue ? "Hesabınız Kalıcı Olarak Askıya Alındı 🚫" : "Hesabınız Geçici Olarak Askıya Alındı 🚫")
                : "İlan İçeriği Uyarısı ⚠️";
            var notifMessage = isBanned
                ? (ban == TimeSpan.MaxValue
                    ? $"Hesabınız kalıcı olarak askıya alındı. Sebep: {violationType} ihlali ({user.ViolationCount}. ihlal)."
                    : $"Hesabınız {user.BannedUntil:dd.MM.yyyy} tarihine kadar askıya alındı. Sebep: {violationType} ihlali ({user.ViolationCount}. ihlal).")
                : $"İlan içeriğinizde kural ihlali tespit edildi ({violationType}). Toplam ihlal: {user.ViolationCount}. Tekrar eden ihlallerde hesabınız askıya alınabilir.";

            // Bildirim gönder — await ile, hata loglanır
            try
            {
                await _publishEndpoint.Publish(new SendNotificationEvent
                {
                    UserId = userId,
                    Type = notifType,
                    Title = notifTitle,
                    Message = notifMessage,
                    ActionUrl = "/panel/ilanlarim",
                    SendEmail = true,
                    UserEmail = user.Email,
                    IdempotencyKey = $"strike-{userId}-{user.ViolationCount}"
                });
            }
            catch (Exception ex)
            {
                await _logService.LogFunctionErrorAsync("MM-NOTIF", ex, new { userId, violationType });
            }
        }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_STRIKE, ex, new { userId, violationType });
            throw;
        }
    }

    private static TimeSpan? CalculateBan(int count) => count switch
    {
        >= 11 => TimeSpan.MaxValue,
        8     => TimeSpan.FromDays(30),
        5     => TimeSpan.FromDays(7),
        _     => null
    };
}
