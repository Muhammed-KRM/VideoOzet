using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly ISettingService _settingService;
    private readonly AppDbContext _db;

    public AdminController(IAdminService adminService, ISettingService settingService, AppDbContext db)
    {
        _adminService = adminService;
        _settingService = settingService;
        _db = db;
    }

    // ─── Sistem Ayarları ─────────────────────────────────────
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        // Örnek olarak bilinen anahtarları döndürelim (veya tümünü veritabanından çekelim)
        // Şimdilik DB'den çekmek daha doğru:
        var settings = new List<VideoOzet.Business.DTOs.GlobalSettingDto>
        {
            new() { Key = "ListingCreationCost", Value = await _settingService.GetSettingAsync("ListingCreationCost", "5"), Description = "İlan oluşturma jeton maliyeti" },
            new() { Key = "MessageUnlockCost", Value = await _settingService.GetSettingAsync("MessageUnlockCost", "1"), Description = "Mesaj kilidi açma jeton maliyeti" },
            new() { Key = "DirectOfferCost", Value = await _settingService.GetSettingAsync("DirectOfferCost", "2"), Description = "Direkt teklif gönderme jeton maliyeti" }
        };
        return Ok(settings);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSetting([FromBody] VideoOzet.Business.DTOs.GlobalSettingDto dto)
    {
        await _settingService.SetSettingAsync(dto.Key, dto.Value, dto.Description);
        return Ok(new { message = "Ayar güncellendi." });
    }

    // ─── Dashboard ───────────────────────────────────────────
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var stats = await _adminService.GetDashboardStatsAsync();
        return Ok(stats);
    }
    // ─── Kullanıcı Yönetimi ──────────────────────────────────
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? search, [FromQuery] string? role, [FromQuery] string? status)
    {
        var users = await _adminService.GetAllUsersAsync(search, role, status);
        return Ok(users);
    }

    [HttpPut("users/{userId}/suspend")]
    public async Task<IActionResult> SuspendUser(Guid userId)
    {
        await _adminService.SuspendUserAsync(userId);
        return Ok(new { message = "Kullanıcı askıya alındı." });
    }

    [HttpPut("users/{userId}/activate")]
    public async Task<IActionResult> ActivateUser(Guid userId)
    {
        await _adminService.ActivateUserAsync(userId);
        return Ok(new { message = "Kullanıcı aktifleştirildi." });
    }

    // ─── İlan Yönetimi ───────────────────────────────────────
    [HttpGet("listings")]
    public async Task<IActionResult> GetListings([FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? type)
    {
        var listings = await _adminService.GetAllListingsAsync(search, status, type);
        return Ok(listings);
    }

    [HttpPut("listings/{listingId}/approve")]
    public async Task<IActionResult> ApproveListing(Guid listingId)
    {
        await _adminService.ApproveListingAsync(listingId);
        return Ok(new { message = "İlan onaylandı." });
    }

    [HttpPut("listings/{listingId}/reject")]
    public async Task<IActionResult> RejectListing(Guid listingId)
    {
        await _adminService.RejectListingAsync(listingId);
        return Ok(new { message = "İlan reddedildi." });
    }

    [HttpPut("listings/{listingId}/suspend")]
    public async Task<IActionResult> SuspendListing(Guid listingId)
    {
        await _adminService.SuspendListingAsync(listingId);
        return Ok(new { message = "İlan askıya alındı." });
    }

    [HttpDelete("listings/{listingId}")]
    public async Task<IActionResult> DeleteListing(Guid listingId)
    {
        await _adminService.DeleteListingAsync(listingId);
        return Ok(new { message = "İlan silindi." });
    }

    // ─── Log Sorguları ───────────────────────────────────────
    [HttpGet("logs/endpoints")]
    public async Task<IActionResult> GetEndpointLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? statusCode = null,
        [FromQuery] string? path = null,
        [FromQuery] string? method = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var query = _db.EndpointLogs.AsQueryable();

        if (statusCode.HasValue) query = query.Where(l => l.StatusCode == statusCode.Value);
        if (!string.IsNullOrEmpty(path)) query = query.Where(l => l.Path.Contains(path));
        if (!string.IsNullOrEmpty(method)) query = query.Where(l => l.Method == method.ToUpper());
        if (userId.HasValue) query = query.Where(l => l.UserId == userId.Value);
        if (from.HasValue) query = query.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(l => l.CreatedAt <= to.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id, l.TraceId, l.Method, l.Path, l.Query,
                l.RequestBody, l.ResponseBody, l.StatusCode,
                l.UserId, l.UserEmail, l.IpAddress, l.DurationMs, l.CreatedAt
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("logs/functions")]
    public async Task<IActionResult> GetFunctionLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? errorCode = null,
        [FromQuery] string? className = null,
        [FromQuery] string? severity = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var query = _db.FunctionLogs.AsQueryable();

        if (!string.IsNullOrEmpty(errorCode)) query = query.Where(l => l.ErrorCode == errorCode);
        if (!string.IsNullOrEmpty(className)) query = query.Where(l => l.ClassName.Contains(className));
        if (!string.IsNullOrEmpty(severity)) query = query.Where(l => l.Severity == severity);
        if (userId.HasValue) query = query.Where(l => l.UserId == userId.Value);
        if (from.HasValue) query = query.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(l => l.CreatedAt <= to.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id, l.ErrorCode, l.ClassName, l.MethodName,
                l.LineNumber, l.ErrorMessage, l.InputType, l.InputValue,
                l.UserId, l.TraceId, l.Severity, l.CreatedAt
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    // ─── Moderasyon / İhlal Yönetimi ─────────────────────────────

    [HttpGet("violations")]
    public async Task<IActionResult> GetViolations(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var items = await _db.ViolationLogs
            .Include(v => v.User)
            .OrderByDescending(v => v.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new {
                v.Id, v.UserId,
                UserEmail = v.User.Email,
                UserName = v.User.FullName,
                v.ListingTitle, v.ViolationType,
                v.DetectedContent, v.DetectedBy,
                v.CreatedAt,
                v.User.ViolationCount,
                v.User.BannedUntil
            })
            .ToListAsync();

        var total = await _db.ViolationLogs.CountAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpPost("users/{userId}/ban")]
    public async Task<IActionResult> BanUser(Guid userId, [FromBody] BanRequestDto dto)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        user.BannedUntil = dto.IsPermanent ? DateTime.MaxValue : DateTime.UtcNow.AddDays(dto.Days);
        user.BanReason = $"Admin: {dto.Reason}";
        await _db.SaveChangesAsync();
        return Ok(new { message = "Ban uygulandı." });
    }

    [HttpPost("users/{userId}/unban")]
    public async Task<IActionResult> UnbanUser(Guid userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        user.BannedUntil = null;
        user.BanReason = null;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Ban kaldırıldı." });
    }

    // ─── SMS Test ─────────────────────────────────────────────
    [HttpPost("test-sms")]
    public async Task<IActionResult> TestSms([FromBody] SmsTestDto dto, [FromServices] ISmsService smsService)
    {
        try
        {
            await smsService.SendAsync(dto.PhoneNumber, dto.Message);
            return Ok(new { message = "SMS test başarılı." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"SMS test başarısız: {ex.Message}" });
        }
    }

    // ─── FCM Test ─────────────────────────────────────────────
    [HttpPost("test-fcm")]
    public async Task<IActionResult> TestFcm([FromBody] FcmTestDto dto, [FromServices] VideoOzet.Business.Infrastructure.Messaging.IFcmService fcmService)
    {
        try
        {
            await fcmService.SendNotificationAsync(dto.Token, dto.Title, dto.Body);
            return Ok(new { message = "FCM test başarılı." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"FCM test başarısız: {ex.Message}" });
        }
    }
}

public record BanRequestDto(bool IsPermanent, int Days, string Reason);
public record SmsTestDto(string PhoneNumber, string Message);
public record FcmTestDto(string Token, string Title, string Body);
