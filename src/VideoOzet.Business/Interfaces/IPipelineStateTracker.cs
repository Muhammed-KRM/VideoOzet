using System;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Interfaces;

public class PipelineItemHealth
{
    public string? SonAsama { get; set; }
    public string? Durum { get; set; }
    public string? Mesaj { get; set; }
    public string? HataMesaji { get; set; }
    public int? Yuzde { get; set; }
    public int? MevcutAdim { get; set; }
    public int? ToplamAdim { get; set; }
    public bool TakiliMi { get; set; }
    public string? TakilmaNedeni { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public interface IPipelineStateTracker
{
    IAsyncDisposable StartHeartbeat(Guid itemId, string stageName, string? extraLockKeyToRenew = null);
    Task RecordQueuedAsync(Guid itemId, Guid? egitimId, string asama, string mesaj);
    Task RecordProgressAsync(PipelineProgressEvent evt);
    Task RecordErrorAsync(Guid itemId, Guid? egitimId, string asama, string hataMesaji);
    Task ClearStateAsync(Guid itemId);
    Task<PipelineItemHealth> EvaluateItemHealthAsync(Guid itemId, string itemType, VideoIslemDurumu islemDurumu, DateTime olusturmaTarihiUtc);
}
