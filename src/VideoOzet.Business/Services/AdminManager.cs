using MassTransit;
using Microsoft.EntityFrameworkCore;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Services;

public class AdminManager : IAdminService
{
    // ═══════════════════════════════════════════════
    // HATA KODLARI — AdminManager (Prefix: ADM)
    // ═══════════════════════════════════════════════
    private const string EC_DASHBOARD      = "ADM-001"; // GetDashboardStatsAsync
    private const string EC_GETUSERS       = "ADM-002"; // GetAllUsersAsync
    private const string EC_SUSPENDUSER    = "ADM-003"; // SuspendUserAsync
    private const string EC_ACTIVATEUSER   = "ADM-004"; // ActivateUserAsync
    private const string EC_GETLISTINGS    = "ADM-005"; // GetAllListingsAsync
    private const string EC_APPROVELISTING = "ADM-006"; // ApproveListingAsync
    private const string EC_REJECTLISTING  = "ADM-007"; // RejectListingAsync
    private const string EC_SUSPENDLISTING = "ADM-008"; // SuspendListingAsync
    private const string EC_DELETELISTING  = "ADM-009"; // DeleteListingAsync
    // ═══════════════════════════════════════════════

    private readonly AppDbContext _context;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogService _logService;

    public AdminManager(AppDbContext context, IPublishEndpoint publishEndpoint, ILogService logService)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
        _logService = logService;
    }

    // ─── Dashboard ───────────────────────────────────────────
    public async Task<AdminDashboardDto> GetDashboardStatsAsync()
    {
        try
        {
        var totalUsers = await _context.Users.CountAsync();
        var totalTeachers = await _context.Users.CountAsync(u => u.IsTeacherProfileComplete);
        var totalListings = await _context.Listings.CountAsync();
        var activeListings = await _context.Listings.CountAsync(l => l.Status == ListingStatus.Active);
        var pendingListings = await _context.Listings.CountAsync(l => l.Status == ListingStatus.Pending);
        var totalMessages = await _context.Messages.CountAsync();
        var totalRevenue = await _context.TokenTransactions
            .Where(t => t.Type == TransactionType.Purchase)
            .SumAsync(t => (decimal)t.Amount);

        return new AdminDashboardDto
        {
            TotalUsers = totalUsers, TotalTeachers = totalTeachers, TotalStudents = totalUsers - totalTeachers,
            TotalListings = totalListings, ActiveListings = activeListings, PendingListings = pendingListings,
            TotalMessages = totalMessages, TotalRevenue = totalRevenue,
            RecentActivities = new List<AdminActivityDto>()
        };
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_DASHBOARD, ex); throw; }
    }

    // ─── Kullanıcı Yönetimi ──────────────────────────────────
    public async Task<List<AdminUserDto>> GetAllUsersAsync(string? search = null, string? role = null, string? status = null)
    {
        try
        {
        var query = _context.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.FullName.Contains(search) || u.Email.Contains(search));
        if (!string.IsNullOrWhiteSpace(role))
        {
            if (role == "Teacher") query = query.Where(u => u.IsTeacherProfileComplete);
            else if (role == "Admin") query = query.Where(u => u.Role == UserRole.Admin);
            else if (role == "Student") query = query.Where(u => !u.IsTeacherProfileComplete && u.Role != UserRole.Admin);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status == "Active") query = query.Where(u => u.IsActive);
            else if (status == "Suspended") query = query.Where(u => !u.IsActive);
        }
        return await query.OrderByDescending(u => u.CreatedAt).Select(u => new AdminUserDto
        {
            Id = u.Id, 
            FullName = u.FullName, 
            Email = u.Email, 
            Phone = u.PhoneEncrypted ?? "—",
            Role = u.Role == UserRole.Admin ? "Admin" : (u.IsTeacherProfileComplete ? "Teacher" : "Student"),
            Status = u.IsActive ? "Active" : "Suspended", 
            IsActive = u.IsActive,
            IsEmailVerified = u.IsEmailVerified,
            IsTeacherProfileComplete = u.IsTeacherProfileComplete,
            TokenBalance = u.TokenBalance,
            ViolationCount = u.ViolationCount,
            BannedUntil = u.BannedUntil,
            BanReason = u.BanReason,
            ProfileImageUrl = u.ProfileImageUrl,
            CreatedAt = u.CreatedAt
        }).ToListAsync();
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_GETUSERS, ex, new { search, role, status }); throw; }
    }

    public async Task SuspendUserAsync(Guid userId)
    {
        try
        {
        var user = await _context.Users.FindAsync(userId);
        if (user != null) { user.IsActive = false; user.UpdatedAt = DateTime.UtcNow; await _context.SaveChangesAsync(); }
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_SUSPENDUSER, ex, userId); throw; }
    }

    public async Task ActivateUserAsync(Guid userId)
    {
        try
        {
        var user = await _context.Users.FindAsync(userId);
        if (user != null) { user.IsActive = true; user.UpdatedAt = DateTime.UtcNow; await _context.SaveChangesAsync(); }
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_ACTIVATEUSER, ex, userId); throw; }
    }

    // ─── İlan Yönetimi ───────────────────────────────────────
    public async Task<List<AdminListingDto>> GetAllListingsAsync(string? search = null, string? status = null, string? type = null)
    {
        try
        {
        var query = _context.Listings.Include(l => l.Owner).Include(l => l.Branch)
            .Include(l => l.District).ThenInclude(d => d.City).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(l => l.Title.Contains(search) || l.Owner.FullName.Contains(search));
        if (!string.IsNullOrWhiteSpace(status))
            if (Enum.TryParse<ListingStatus>(status, out var parsed)) query = query.Where(l => l.Status == parsed);
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (type == "TeacherOffering") query = query.Where(l => l.Type == ListingType.TeacherOffering);
            else if (type == "StudentLooking") query = query.Where(l => l.Type == ListingType.StudentLooking);
        }
        return await query.OrderByDescending(l => l.CreatedAt).Select(l => new AdminListingDto
        {
            Id = l.Id, Title = l.Title, TeacherName = l.Owner.FullName, Branch = l.Branch.Name,
            City = l.District.City.Name, HourlyPrice = l.HourlyPrice, Status = l.Status.ToString(),
            Type = l.Type.ToString(), IsVitrin = l.IsVitrin, ViewCount = l.ReviewCount, MessageCount = 0, CreatedAt = l.CreatedAt
        }).ToListAsync();
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_GETLISTINGS, ex, new { search, status, type }); throw; }
    }

    public async Task ApproveListingAsync(Guid listingId)
    {
        try
        {
        var listing = await _context.Listings.Include(l => l.Owner).FirstOrDefaultAsync(l => l.Id == listingId);
        if (listing != null)
        {
            listing.Status = ListingStatus.Active;
            await _context.SaveChangesAsync();

            // Onay bildirimi
            if (listing.Owner != null)
                _ = _publishEndpoint.Publish(new SendNotificationEvent
                {
                    UserId = listing.OwnerId,
                    Type = "ListingApproved",
                    Title = "İlanınız Onaylandı ✅",
                    Message = $"\"{listing.Title}\" başlıklı ilanınız onaylandı ve yayında.",
                    ActionUrl = $"/ilan/{listing.Slug}",
                    SendEmail = true,
                    UserEmail = listing.Owner.Email
                });
        }
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_APPROVELISTING, ex, listingId); throw; }
    }

    public async Task RejectListingAsync(Guid listingId)
    {
        try
        {
        var listing = await _context.Listings.Include(l => l.Owner).FirstOrDefaultAsync(l => l.Id == listingId);
        if (listing != null)
        {
            listing.Status = ListingStatus.Suspended;
            await _context.SaveChangesAsync();

            // Red bildirimi
            if (listing.Owner != null)
                _ = _publishEndpoint.Publish(new SendNotificationEvent
                {
                    UserId = listing.OwnerId,
                    Type = "ListingRejected",
                    Title = "İlanınız Reddedildi ❌",
                    Message = $"\"{listing.Title}\" başlıklı ilanınız admin tarafından reddedildi. Düzenleyerek tekrar gönderebilirsiniz.",
                    ActionUrl = "/panel/ilanlarim",
                    SendEmail = true,
                    UserEmail = listing.Owner.Email
                });
        }
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_REJECTLISTING, ex, listingId); throw; }
    }

    public async Task SuspendListingAsync(Guid listingId)
    {
        try
        {
        var listing = await _context.Listings.FindAsync(listingId);
        if (listing != null) { listing.Status = ListingStatus.Suspended; await _context.SaveChangesAsync(); }
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_SUSPENDLISTING, ex, listingId); throw; }
    }

    public async Task DeleteListingAsync(Guid listingId)
    {
        try
        {
        var listing = await _context.Listings.FindAsync(listingId);
        if (listing != null) { _context.Listings.Remove(listing); await _context.SaveChangesAsync(); }
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_DELETELISTING, ex, listingId); throw; }
    }
}
