using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    
    // Öğretmen olarak bilgilerini tamamlamış mı? Zaten tek tip kullanıcı var.
    public bool IsTeacherProfileComplete { get; set; } 
    
    public UserRole Role { get; set; } = UserRole.User;
    
    // AES-256 Şifreli veya null olabilir
    public string? PhoneEncrypted { get; set; }
    public string? TCKNEncrypted { get; set; }
    public string? IBANEncrypted { get; set; }
    
    public string? ProfileImageUrl { get; set; }
    public string? Bio { get; set; }
    public int TokenBalance { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsEmailVerified { get; set; }
    // Profil Ayarları
    public DateTime? BirthDate { get; set; }
    public bool EmailNotifications { get; set; } = true;
    public bool MarketingEmails { get; set; } = false;
    
    // FCM Push Notification
    public string? FcmToken { get; set; }
    public DateTime? FcmTokenUpdatedAt { get; set; }
    public bool SmsNotifications { get; set; } = true;

    // Refresh Token for JWT
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Moderasyon alanları
    public int ViolationCount { get; set; } = 0;
    public DateTime? BannedUntil { get; set; }
    public DateTime? LastViolationAt { get; set; }
    public string? BanReason { get; set; }

    // Navigation Properties
    // Öğretmen veya öğrenci olarak kullanıcının açtığı tüm ilanlar
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
    public ICollection<TokenTransaction> TokenTransactions { get; set; } = new List<TokenTransaction>();
    public ICollection<Message> SentMessages { get; set; } = new List<Message>();
    public ICollection<Message> ReceivedMessages { get; set; } = new List<Message>();
    public ICollection<Review> GivenReviews { get; set; } = new List<Review>();
    public ICollection<Review> ReceivedReviews { get; set; } = new List<Review>();
}
