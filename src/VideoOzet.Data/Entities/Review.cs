namespace VideoOzet.Data.Entities;

public class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid ReviewerId { get; set; } // Yorumu yapan
    public Guid ReviewedId { get; set; } // Yorum yapılan
    public Guid ListingId { get; set; }
    
    public int ProfessionalismRating { get; set; } // 1-5
    public int CommunicationRating { get; set; }   // 1-5
    public int ValueRating { get; set; }           // 1-5
    public double AverageRating => (ProfessionalismRating + CommunicationRating + ValueRating) / 3.0;
    
    public string Content { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public User Reviewer { get; set; } = null!;
    public User Reviewed { get; set; } = null!;
    public Listing Listing { get; set; } = null!;
}
