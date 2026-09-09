namespace VideoOzet.Data.Entities;

public class Notification
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Type { get; set; } = "";
    // Tipler: ListingPending, ListingApproved, ListingRejected,
    //         NewApplication, TokenLoaded, Warning, Ban,
    //         MessageReceived, VitrinExpired, PasswordChanged, Welcome
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// Duplicate önleme: aynı (UserId, Type, IdempotencyKey) kombinasyonu tekrar yazılmaz.
    /// Örnek: "listing-{listingId}", "token-{transactionId}", "strike-{violationLogId}"
    /// </summary>
    public string? IdempotencyKey { get; set; }
    /// <summary>Otomatik temizleme için — 90 gün sonra silinir</summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(90);
}
