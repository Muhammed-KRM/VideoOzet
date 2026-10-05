using System;
using System.Collections.Generic;
using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class SeriBolum
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeriPlaniId { get; set; }
    public int BolumNo { get; set; }
    public string CalismaBasligi { get; set; } = string.Empty;
    public string AnaFikir { get; set; } = string.Empty;
    public int HedefSureDk { get; set; }
    public string KonularJson { get; set; } = string.Empty;
    public Guid? AktifRevizyonId { get; set; }
    public bool BaglamEskidi { get; set; }
    public string BaglamSorunlariJson { get; set; } = string.Empty;
    public BolumDurumu Durum { get; set; }

    public SeriPlani SeriPlani { get; set; } = null!;
    public ICollection<BolumRevizyonu> Revizyonlar { get; set; } = new List<BolumRevizyonu>();
}
