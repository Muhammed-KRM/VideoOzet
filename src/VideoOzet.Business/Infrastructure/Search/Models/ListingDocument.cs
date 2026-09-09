using System;

namespace VideoOzet.Business.Infrastructure.Search.Models;

public class ListingDocument
{
    public string Id { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string TeacherName { get; set; } = null!;
    public string BranchSlug { get; set; } = null!;
    public string BranchName { get; set; } = null!;
    public string CitySlug { get; set; } = null!;
    public string DistrictSlug { get; set; } = null!;
    public int HourlyPrice { get; set; }
    public string LessonType { get; set; } = null!;
    public bool IsVitrin { get; set; }
    public DateTime? VitrinExpiresAt { get; set; }
    public float AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
