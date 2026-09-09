using System;

namespace VideoOzet.Data.Entities;

public class VideoSummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VideoId { get; set; }
    public string OzetMetni { get; set; } = string.Empty;
    public string KonuBasliklari { get; set; } = "[]";
    public string KonuEtiketleri { get; set; } = "[]";
    public string LlmModel { get; set; } = string.Empty;
    public int TokenKullanimi { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    public Video Video { get; set; } = null!;
}
