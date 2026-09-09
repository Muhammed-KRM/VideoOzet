using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Interfaces;

public interface IAdminService
{
    // Dashboard
    Task<AdminDashboardDto> GetDashboardStatsAsync();

    // Kullanıcı Yönetimi
    Task<List<AdminUserDto>> GetAllUsersAsync(string? search = null, string? role = null, string? status = null);
    Task SuspendUserAsync(Guid userId);
    Task ActivateUserAsync(Guid userId);

    // İlan Yönetimi
    Task<List<AdminListingDto>> GetAllListingsAsync(string? search = null, string? status = null, string? type = null);
    Task ApproveListingAsync(Guid listingId);
    Task RejectListingAsync(Guid listingId);
    Task SuspendListingAsync(Guid listingId);
    Task DeleteListingAsync(Guid listingId);
}

public class AdminDashboardDto
{
    public int TotalUsers { get; set; }
    public int TotalTeachers { get; set; }
    public int TotalStudents { get; set; }
    public int TotalListings { get; set; }
    public int ActiveListings { get; set; }
    public int PendingListings { get; set; }
    public int TotalMessages { get; set; }
    public decimal TotalRevenue { get; set; }
    public List<AdminActivityDto> RecentActivities { get; set; } = new();
}
