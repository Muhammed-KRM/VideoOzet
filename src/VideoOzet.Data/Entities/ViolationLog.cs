namespace VideoOzet.Data.Entities;

public class ViolationLog
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? ListingId { get; set; }
    public string ListingTitle { get; set; } = "";
    public string ViolationType { get; set; } = ""; // "Phone", "Email", "Link"
    public string DetectedContent { get; set; } = "";
    public string DetectedBy { get; set; } = "";    // "Regex", "Ollama", "Admin"
    public bool IsManual { get; set; } = false;
    public string? AdminNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
