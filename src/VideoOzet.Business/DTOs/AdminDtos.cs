using System;

namespace VideoOzet.Business.DTOs;

public class AdminActivityDto
{
    public string Icon { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class PackageSaleDto
{
    public string Name { get; set; } = string.Empty;
    public int SalesCount { get; set; }
    public int Percentage { get; set; }
}

public class AdminBranchDto
{
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ListingCount { get; set; }
}

public class AdminUserDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsTeacherProfileComplete { get; set; }
    public int TokenBalance { get; set; }
    public int ViolationCount { get; set; }
    public DateTime? BannedUntil { get; set; }
    public string? BanReason { get; set; }
    public string? ProfileImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminListingDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public int HourlyPrice { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsVitrin { get; set; }
    public int ViewCount { get; set; }
    public int MessageCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
