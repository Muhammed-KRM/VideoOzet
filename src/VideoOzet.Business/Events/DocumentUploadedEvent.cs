using System;

namespace VideoOzet.Business.Events;

public class DocumentUploadedEvent
{
    public Guid DokumanId { get; set; }
    public Guid EgitimId { get; set; }
    public string DosyaYolu { get; set; } = string.Empty;
    public string Uzanti { get; set; } = string.Empty;
}
