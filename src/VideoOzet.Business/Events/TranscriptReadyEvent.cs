using System;

namespace VideoOzet.Business.Events;

public class TranscriptReadyEvent
{
    public Guid VideoId { get; set; }
    public Guid EgitimId { get; set; }
    public Guid TranscriptId { get; set; }
}
