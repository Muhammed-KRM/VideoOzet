using System;

namespace VideoOzet.Business.Events;

public class DocumentTextReadyEvent
{
    public Guid DokumanId { get; set; }
    public Guid EgitimId { get; set; }
    public Guid DokumanMetinId { get; set; }
}
