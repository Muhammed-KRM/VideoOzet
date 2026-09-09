using VideoOzet.Data.Enums;

namespace VideoOzet.Business.DTOs;

public class MessageDto
{
    public Guid Id { get; set; }
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string? SenderImageUrl { get; set; }
    public Guid ReceiverId { get; set; }
    public Guid? ListingId { get; set; }
    public string? ListingTitle { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsInitiatedWithToken { get; set; }
    public bool IsUnlocked { get; set; }
    public MessageStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class MessageSendDto
{
    public Guid ReceiverId { get; set; }
    public Guid? ListingId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsDirectOffer { get; set; } // Senaryo B: Jeton harcayarak direkt teklif mi?
}
