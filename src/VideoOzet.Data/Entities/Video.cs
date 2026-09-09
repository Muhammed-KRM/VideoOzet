using System;
using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class Video
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EgitimId { get; set; }
    public string Baslik { get; set; } = string.Empty;
    public string DosyaYolu { get; set; } = string.Empty;
    public string? SesYolu { get; set; }
    public TimeSpan? Sure { get; set; }
    public int Sira { get; set; }
    public long DosyaBoyutu { get; set; }
    public VideoIslemDurumu IslemDurumu { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? IslemTamamlanmaTarihi { get; set; }

    public Egitim Egitim { get; set; } = null!;
    public VideoTranscript? Transcript { get; set; }
    public VideoSummary? Summary { get; set; }
}
