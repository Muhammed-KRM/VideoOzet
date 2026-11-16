using System;
using Pgvector;

namespace VideoOzet.Data.Entities;

public class VideoChunkDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? VideoId { get; set; }
    public Guid? DokumanId { get; set; }
    public Guid EgitimId { get; set; }
    public int StartTimeMs { get; set; }
    public int EndTimeMs { get; set; }
    public int ChunkIndex { get; set; }
    public VideoOzet.Data.Enums.ChunkTipi ChunkTipi { get; set; } = VideoOzet.Data.Enums.ChunkTipi.HamMetin;
    public string KaynakBaslik { get; set; } = string.Empty;
    public string? BolumBasligi { get; set; }
    public string KaynakDil { get; set; } = "tr";
    public VideoOzet.Data.Enums.KaynakKullanimTuru KullanimTuru { get; set; } = VideoOzet.Data.Enums.KaynakKullanimTuru.DogrudanReferans;
    public string Text { get; set; } = string.Empty;
    public Vector? Embedding { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
