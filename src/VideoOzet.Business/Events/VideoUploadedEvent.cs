using System;

namespace VideoOzet.Business.Events;

public class VideoUploadedEvent
{
    public Guid VideoId { get; set; }
    public Guid EgitimId { get; set; }
    public string DosyaYolu { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
