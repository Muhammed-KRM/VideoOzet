namespace VideoOzet.Data.Entities;

public class Branch
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string? Category { get; set; }   // "Akademik", "Yazılım", "Müzik", "Spor" vb.
    public bool IsPopular { get; set; }
    public int DisplayOrder { get; set; }
    
    // Navigation Property
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
