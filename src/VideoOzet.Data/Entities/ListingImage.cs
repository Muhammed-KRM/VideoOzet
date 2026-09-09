namespace VideoOzet.Data.Entities;

public class ListingImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsMain { get; set; }
    public int DisplayOrder { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Property
    public Listing Listing { get; set; } = null!;
}
