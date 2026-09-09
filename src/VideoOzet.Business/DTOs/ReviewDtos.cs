namespace VideoOzet.Business.DTOs;

public class ReviewDto
{
    public Guid Id { get; set; }
    public Guid ReviewerId { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
    public string? ReviewerImageUrl { get; set; }
    public int ProfessionalismRating { get; set; }
    public int CommunicationRating { get; set; }
    public int ValueRating { get; set; }
    public double AverageRating { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ReviewCreateDto
{
    public Guid ListingId { get; set; }
    public int ProfessionalismRating { get; set; }
    public int CommunicationRating { get; set; }
    public int ValueRating { get; set; }
    public string ReviewText { get; set; } = string.Empty;
}
