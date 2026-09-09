using System;

namespace VideoOzet.Business.Events;

public class ContentGeneratedEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
    public string Konu { get; set; } = string.Empty;
}
