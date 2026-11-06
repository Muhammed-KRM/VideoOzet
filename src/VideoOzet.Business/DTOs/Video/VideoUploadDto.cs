using System;
using System.IO;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.DTOs.Video;

public class VideoUploadDto
{
    public Guid EgitimId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public Stream FileStream { get; set; } = Stream.Null;
    public KaynakKullanimTuru KullanimTuru { get; set; } = KaynakKullanimTuru.DogrudanReferans;
}
