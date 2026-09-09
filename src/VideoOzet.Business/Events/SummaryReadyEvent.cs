using System;

namespace VideoOzet.Business.Events;

public class SummaryReadyEvent
{
    public Guid VideoId { get; set; }
    public Guid EgitimId { get; set; }
    public Guid SummaryId { get; set; }
}
