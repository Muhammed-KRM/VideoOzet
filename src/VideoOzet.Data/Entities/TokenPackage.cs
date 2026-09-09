namespace VideoOzet.Data.Entities;

public class TokenPackage
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public decimal Price { get; set; }
    public decimal PricePerToken => Price / TokenCount;
    public bool IsPopular { get; set; }
    public string? BadgeText { get; set; }
}
