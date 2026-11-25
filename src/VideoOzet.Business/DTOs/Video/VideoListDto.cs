using System;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.DTOs.Video;

public class VideoListDto
{
    public Guid Id { get; set; }
    public Guid EgitimId { get; set; }
    public string Baslik { get; set; } = string.Empty;
    public string DosyaYolu { get; set; } = string.Empty;
    public string? Transkript { get; set; }
    public string? Ozet { get; set; }
    public VideoIslemDurumu IslemDurumu { get; set; }
    public KaynakKullanimTuru KullanimTuru { get; set; } = KaynakKullanimTuru.DogrudanReferans;
    public IndekslemeTuru IndekslemeTuru { get; set; } = IndekslemeTuru.HamKaynak;
    public string? OrijinalDil { get; set; } = "tr";
    public string? SttModel { get; set; }
    public int? TranskriptKaliteSkoru { get; set; }
    public string? TranskriptKaynakNotu { get; set; }
    public KaynakPlatform KaynakPlatform { get; set; } = KaynakPlatform.Yerel;
    public string? KaynakUrl { get; set; }
    public string? DisKaynakId { get; set; }
    public string? KanalAdi { get; set; }
    public string? ThumbnailUrl { get; set; }
    public UzaktanAlimModu AlimModu { get; set; } = UzaktanAlimModu.AkilliTranskriptOncelikli;
    public DateTime OlusturmaTarihi { get; set; }

    public string? SonAsama { get; set; }
    public string? Mesaj { get; set; }
    public string? HataMesaji { get; set; }
    public int? Yuzde { get; set; }
    public int? MevcutAdim { get; set; }
    public int? ToplamAdim { get; set; }
    public bool TakiliMi { get; set; }
    public string? TakilmaNedeni { get; set; }
    public DateTime? SonGuncellemeTarihi { get; set; }
}
