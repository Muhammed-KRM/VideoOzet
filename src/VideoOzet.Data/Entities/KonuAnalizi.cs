using System;
using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class KonuAnalizi
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentRequestId { get; set; }
    public string AnaFikir { get; set; } = string.Empty;
    public string BeklenenKonularJson { get; set; } = string.Empty;
    public string KonuHaritasiJson { get; set; } = string.Empty;
    public string KaynaktaOlmayanlarJson { get; set; } = string.Empty;
    public int OnerilenVideoSayisi { get; set; }
    public int OneriSuresiDk { get; set; }
    public string OneriGerekcesi { get; set; } = string.Empty;
    public AnalizDurumu Durum { get; set; }
    public string LlmModel { get; set; } = string.Empty;

    public ContentRequest ContentRequest { get; set; } = null!;
}
