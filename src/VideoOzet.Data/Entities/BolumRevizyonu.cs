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
    public int ToplamIddiaSayisi { get; set; }
    public int DesteklenenSayisi { get; set; }
    public int BelirsizSayisi { get; set; }
    public int DesteklenmeyenSayisi { get; set; }
    public string DetayliRapor { get; set; } = "[]";
    public BolumDurumu QcDurumu { get; set; } = BolumDurumu.Bekliyor;
    public string LlmModel { get; set; } = string.Empty;
    public BolumDurumu Durum { get; set; }

    public SeriBolum SeriBolum { get; set; } = null!;
}
