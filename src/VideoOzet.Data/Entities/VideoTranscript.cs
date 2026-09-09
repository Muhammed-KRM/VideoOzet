using System;

namespace VideoOzet.Data.Entities;

public class VideoTranscript
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VideoId { get; set; }
    public string HamMetin { get; set; } = string.Empty;
    public string? ZamanDamgalari { get; set; }
    public string Dil { get; set; } = "tr";
    public int KelimeSayisi { get; set; }
    public string SttModel { get; set; } = string.Empty;
    public int SttSuresiMs { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    public Video Video { get; set; } = null!;
}
