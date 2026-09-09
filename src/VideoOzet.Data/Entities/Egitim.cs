using System;
using System.Collections.Generic;
using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class Egitim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? GuncellemeTarihi { get; set; }
    public int ToplamVideoSayisi { get; set; }
    public int IslenmiVideoSayisi { get; set; }
    public EgitimDurumu Durum { get; set; }

    public ICollection<Video> Videolar { get; set; } = new List<Video>();
    public ICollection<ContentRequest> ContentRequests { get; set; } = new List<ContentRequest>();
}
