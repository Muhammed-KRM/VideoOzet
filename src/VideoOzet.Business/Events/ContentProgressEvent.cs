using System;

namespace VideoOzet.Business.Events;

public class ContentProgressEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
    public string Asama { get; set; } = string.Empty; // "İçerik Üretiliyor", "Kalite Kontrol Yapılıyor" vb.
    public string Durum { get; set; } = string.Empty; // "İşleniyor", "Tamamlandı" vb.
    public int Yuzde { get; set; }
}
