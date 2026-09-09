using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }
    public Guid? ListingId { get; set; } // Hangi ilan üzerinden başlatıldı? Direkt teklifse null olabilir
    
    public string Content { get; set; } = string.Empty;
    
    // Mesaj başlatılırken jeton harcanarak mı gönderildi? (Senaryo B - Direkt Reklam)
    public bool IsInitiatedWithToken { get; set; }
    
    public MessageStatus Status { get; set; } = MessageStatus.Sent;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }

    // Navigation Properties
    public User Sender { get; set; } = null!;
    public User Receiver { get; set; } = null!;
    public Listing? Listing { get; set; }
}
