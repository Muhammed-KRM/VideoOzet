using System;
using System.Collections.Generic;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.DTOs;

public class KaynakIcerikDetayDto
{
    public Guid Id { get; set; }
    public string Tip { get; set; } = "video"; // "video" | "dokuman"
    public string Baslik { get; set; } = string.Empty;
    public KaynakKullanimTuru KullanimTuru { get; set; }
    public string OrijinalDil { get; set; } = "tr";
    public int KelimeSayisi { get; set; }
    public string? SttModel { get; set; }
    public string? LlmModel { get; set; }
    public DateTime? OlusturmaTarihi { get; set; }

    public string HamMetin { get; set; } = string.Empty;            // Ham Transkript veya Doküman Metni
    public string? DamitilmisOzetMetni { get; set; }                // Orijinal Damıtılmış Özet
    public string? CevrilmisOzetMetni { get; set; }                 // Terim Korumalı Türkçe Çeviri (Yabancıysa)
    public List<string> KonuBasliklari { get; set; } = new();
    public List<string> KonuEtiketleri { get; set; } = new();
}

public class UpdateKullanimTuruDto
{
    public KaynakKullanimTuru KullanimTuru { get; set; }
}
