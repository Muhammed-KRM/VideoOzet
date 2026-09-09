using System;

namespace VideoOzet.Business.Events;

public class ContentRequestedEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
    public string Konu { get; set; } = string.Empty;
    public string? HedefUzunluk { get; set; }
    public string? HedefKitle { get; set; }
}
