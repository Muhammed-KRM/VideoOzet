using System;

namespace VideoOzet.Business.Events;

public class PipelineProgressEvent
{
    public Guid? EgitimId { get; set; }
    public Guid? VideoId { get; set; }
    public string Asama { get; set; } = string.Empty;
    public string Durum { get; set; } = string.Empty;
    public string Mesaj { get; set; } = string.Empty;
}
