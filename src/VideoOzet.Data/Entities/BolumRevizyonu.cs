using System;
using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class BolumRevizyonu
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeriBolumId { get; set; }
    public int RevizyonNo { get; set; }
    public RevizyonTipi Tip { get; set; }
    public string Talimat { get; set; } = string.Empty;
    public string HedefAlan { get; set; } = string.Empty;
    public string ArastirmaOzeti { get; set; } = string.Empty;
    public string VideoPlani { get; set; } = string.Empty;
    public string DevirNotuJson { get; set; } = string.Empty;
    public string BaglamJson { get; set; } = string.Empty;
    public string KullanilanKaynaklar { get; set; } = string.Empty;
    public decimal GuvenSkorYuzde { get; set; }
    public string LlmModel { get; set; } = string.Empty;
    public BolumDurumu Durum { get; set; }

    public SeriBolum SeriBolum { get; set; } = null!;
}
