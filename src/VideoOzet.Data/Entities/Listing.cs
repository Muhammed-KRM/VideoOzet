using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class Listing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    // İlanı veren kişi (Öğrenci de olabilirÖğretmen de)
    public Guid OwnerId { get; set; } 
    public ListingType Type { get; set; } 
    
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int HourlyPrice { get; set; }
    public LessonType LessonType { get; set; }
    
    public int BranchId { get; set; }
    public int DistrictId { get; set; }
    
    public bool IsVitrin { get; set; }
    public DateTime? VitrinExpiresAt { get; set; }
    
    public ListingStatus Status { get; set; } = ListingStatus.Pending;
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Madde 7 — Yeni alanlar
    public string? EducationLevel { get; set; }       // "İlkokul", "Ortaokul", "Lise", "Üniversite", "Yetişkin"
    public int? ExperienceYears { get; set; }          // 0-30
    public int LessonDurationMinutes { get; set; } = 60; // 45, 60, 90, 120
    public bool IsGroupLesson { get; set; } = false;
    public int? MaxGroupSize { get; set; }
    public bool HasTrialLesson { get; set; } = false;
    public string? EducationBackground { get; set; }  // "İTÜ Matematik Mühendisliği mezunu"

    // Sınıf aralığı — 0=Anasınıfı, 1-8=İlk/Orta, 9-12=Lise, 13=Üniversite, 14=Mezun/Yetişkin
    public int? GradeMin { get; set; }
    public int? GradeMax { get; set; }

    // Navigation Properties
    public User Owner { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public District District { get; set; } = null!;
    
    public ICollection<ListingImage> Images { get; set; } = new List<ListingImage>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
