using System;
using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class PipelineLog
{
    public long Id { get; set; }
    public Guid? VideoId { get; set; }
    public Guid? EgitimId { get; set; }
    public Guid? ContentRequestId { get; set; }
    public PipelineAsamasi Asama { get; set; }
    public string Durum { get; set; } = string.Empty;
    public DateTime BaslangicZamani { get; set; } = DateTime.UtcNow;
    public DateTime? BitisZamani { get; set; }
    public int? SureMs { get; set; }
    public string? HataMesaji { get; set; }
    public string? HataDetayi { get; set; }
    public string? GirdiMetadata { get; set; }
    public string? CiktiMetadata { get; set; }
    public string? TraceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
