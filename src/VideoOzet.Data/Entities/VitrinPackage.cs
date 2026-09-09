namespace VideoOzet.Data.Entities;

public class VitrinPackage
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationInDays { get; set; }
    public decimal Price { get; set; }
    
    public bool IncludesAmberGlow { get; set; }
    public bool IncludesTopRanking { get; set; }
    public bool IncludesHomeCarousel { get; set; }
}
