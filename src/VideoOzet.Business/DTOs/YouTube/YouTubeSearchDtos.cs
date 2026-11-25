using System.Collections.Generic;

namespace VideoOzet.Business.DTOs.YouTube;

public class YouTubeSearchItemDto
{
    public string VideoId { get; set; } = string.Empty;
    public string Baslik { get; set; } = string.Empty;
    public string KanalAdi { get; set; } = string.Empty;
    public string? KanalId { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public int? SureSaniye { get; set; }
    public string SureMetni { get; set; } = string.Empty;
    public string? YayinTarihi { get; set; }
    public long? GoruntulenmeSayisi { get; set; }
    public string? Aciklama { get; set; }
    public string KaynakUrl { get; set; } = string.Empty;
}

public class YouTubeSearchResponseDto
{
    public List<YouTubeSearchItemDto> Items { get; set; } = new();
    /// <summary>"youtube-api-v3" veya "yt-dlp"</summary>
    public string KullanilanKaynak { get; set; } = "yt-dlp";
    public bool KotaAsildiMi { get; set; }
    public string? UyariMesaji { get; set; }
}

public class YouTubeVideoDetailDto
{
    public string VideoId { get; set; } = string.Empty;
    public string Baslik { get; set; } = string.Empty;
    public string Aciklama { get; set; } = string.Empty;
    public string KanalAdi { get; set; } = string.Empty;
    public string? KanalId { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public int? SureSaniye { get; set; }
    public string SureMetni { get; set; } = string.Empty;
    public string? YayinTarihi { get; set; }
    public long? GoruntulenmeSayisi { get; set; }
    public List<string> ManuelAltyaziDilleri { get; set; } = new();
    public List<string> OtomatikAltyaziDilleri { get; set; } = new();
    public List<YouTubeSearchItemDto> IlgiliVideolar { get; set; } = new();
    public string KaynakUrl { get; set; } = string.Empty;
}
