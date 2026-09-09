namespace VideoOzet.Data.Entities;

public class District
{
    public int Id { get; set; }
    public int CityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    
    // Navigation Properties
    public City City { get; set; } = null!;
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
