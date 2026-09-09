namespace VideoOzet.Business.DTOs;

public class AdminDashboardStatsDto
{
    public int TotalUsers { get; set; }
    public int TotalListings { get; set; }
    public int TotalMessages { get; set; }
    public decimal TotalRevenue { get; set; }
    public int ActiveUsers { get; set; }
    public int PendingListings { get; set; }
    public int TotalViolations { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
