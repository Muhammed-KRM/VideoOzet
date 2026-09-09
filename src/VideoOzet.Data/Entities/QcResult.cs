using System;

namespace VideoOzet.Data.Entities;

public class QcResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentRequestId { get; set; }
    public int ToplamIddiaSayisi { get; set; }
    public int DesteklenenSayisi { get; set; }
    public int BelirsizSayisi { get; set; }
    public int DesteklenmeyenSayisi { get; set; }
    public string DetayliRapor { get; set; } = "[]";
    public decimal GuvenSkorYuzde { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    public ContentRequest ContentRequest { get; set; } = null!;
}
