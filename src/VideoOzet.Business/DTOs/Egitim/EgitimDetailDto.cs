using System;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.DTOs.Egitim;

public class EgitimDetailDto
{
    public Guid Id { get; set; }
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public DateTime OlusturmaTarihi { get; set; }
    public DateTime? GuncellemeTarihi { get; set; }
    public int ToplamVideoSayisi { get; set; }
    public int IslenmiVideoSayisi { get; set; }
    public EgitimDurumu Durum { get; set; }
}
