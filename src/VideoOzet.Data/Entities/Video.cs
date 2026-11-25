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
    public KaynakKullanimTuru KullanimTuru { get; set; } = KaynakKullanimTuru.DogrudanReferans;
    public IndekslemeTuru IndekslemeTuru { get; set; } = IndekslemeTuru.HamKaynak;
    public KaynakPlatform KaynakPlatform { get; set; } = KaynakPlatform.Yerel;
    public string? KaynakUrl { get; set; }
    public string? DisKaynakId { get; set; }
    public string? KanalAdi { get; set; }
    public string? ThumbnailUrl { get; set; }
    public UzaktanAlimModu AlimModu { get; set; } = UzaktanAlimModu.AkilliTranskriptOncelikli;
    public int? TranskriptKaliteSkoru { get; set; }
    public string? TranskriptKaynakNotu { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? IslemTamamlanmaTarihi { get; set; }

    public Egitim Egitim { get; set; } = null!;
    public VideoTranscript? Transcript { get; set; }
    public VideoSummary? Summary { get; set; }
}
