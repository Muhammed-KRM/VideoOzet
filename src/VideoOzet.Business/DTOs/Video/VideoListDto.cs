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
    public string? OrijinalDil { get; set; } = "tr";
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
