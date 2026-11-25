using System;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Events;

public class RemoteVideoRequestedEvent
{
    public Guid VideoId { get; set; }
    public Guid EgitimId { get; set; }
    public string KaynakUrl { get; set; } = string.Empty;
    public KaynakPlatform KaynakPlatform { get; set; } = KaynakPlatform.YouTube;
    public UzaktanAlimModu AlimModu { get; set; } = UzaktanAlimModu.AkilliTranskriptOncelikli;
    public string TercihEdilenDil { get; set; } = "tr";
    public string? AuthHeader { get; set; }
    public string? TranscriptUrl { get; set; }
}
