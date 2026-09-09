namespace VideoOzet.Data.Entities;

public class City
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int PlateCode { get; set; }
    
    // Navigation Property
    public ICollection<District> Districts { get; set; } = new List<District>();
}
