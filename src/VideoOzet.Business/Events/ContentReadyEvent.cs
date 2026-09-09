using System;

namespace VideoOzet.Business.Events;

public class ContentReadyEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
}
