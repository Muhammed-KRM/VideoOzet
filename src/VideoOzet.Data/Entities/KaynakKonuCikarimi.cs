using System;

namespace VideoOzet.Data.Entities;

public class KaynakKonuCikarimi
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid KaynakId { get; set; }
    public string KaynakTuru { get; set; } = string.Empty;
    public string KonularJson { get; set; } = string.Empty;
    public int PromptVersiyonu { get; set; }
    public string? IcerikHash { get; set; }
}
