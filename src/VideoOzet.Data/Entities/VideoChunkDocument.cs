using System;
using Pgvector;

namespace VideoOzet.Data.Entities;

public class VideoChunkDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VideoId { get; set; }
    public Guid EgitimId { get; set; }
    public int StartTimeMs { get; set; }
    public int EndTimeMs { get; set; }
    public string Text { get; set; } = string.Empty;
    public Vector? Embedding { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
