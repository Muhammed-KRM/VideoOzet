using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class TokenTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    
    public TransactionType Type { get; set; }
    
    // Satın alımlarda artı (+), harcamalarda eksi (-) değer.
    public int Amount { get; set; }
    
    public string Description { get; set; } = string.Empty;
    public string? ReferenceId { get; set; } // Ödeme sistemi Id veya Mesaj Id
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Property
    public User User { get; set; } = null!;
}
