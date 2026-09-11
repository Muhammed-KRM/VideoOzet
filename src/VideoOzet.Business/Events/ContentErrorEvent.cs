using System;

namespace VideoOzet.Business.Events;

public class ContentErrorEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
    public string HataMesaji { get; set; } = string.Empty;
}
