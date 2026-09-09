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
    public DateTime OlusturmaTarihi { get; set; }
}
