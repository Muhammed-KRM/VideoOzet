using VideoOzet.Data.Enums;

namespace VideoOzet.Business.DTOs;

public class TokenTransactionDto
{
    public Guid Id { get; set; }
    public TransactionType Type { get; set; }
    public int Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int BalanceAfter { get; set; }
}

public class TokenPackageDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public decimal Price { get; set; }
    public decimal PricePerToken => Price / TokenCount;
    public bool IsPopular { get; set; }
    public string? BadgeText { get; set; }
    public string Icon { get; set; } = string.Empty;
    public string Badge { get; set; } = string.Empty;
}

public class VitrinPackageDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationInDays { get; set; }
    public decimal Price { get; set; }
    public bool IncludesAmberGlow { get; set; }
    public bool IncludesTopRanking { get; set; }
    public bool IncludesHomeCarousel { get; set; }
}
