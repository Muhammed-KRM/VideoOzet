using System;

namespace VideoOzet.Data.Entities;

public class ContentVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentRequestId { get; set; }
    public int VersiyonNo { get; set; } = 1;
    public string ArastirmaOzeti { get; set; } = string.Empty;
    public string VideoPlani { get; set; } = string.Empty;
    public string? RevizeTalimati { get; set; }
    public string LlmModel { get; set; } = string.Empty;
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    // Versiyona Özel Kalite Kontrol (QC) Bilgileri
    public int? ToplamIddiaSayisi { get; set; }
    public int? DesteklenenSayisi { get; set; }
    public int? BelirsizSayisi { get; set; }
    public int? DesteklenmeyenSayisi { get; set; }
    public string? DetayliRapor { get; set; } = "[]";
    public decimal? GuvenSkorYuzde { get; set; }
    public DateTime? QcTarihi { get; set; }

    public ContentRequest ContentRequest { get; set; } = null!;
}
