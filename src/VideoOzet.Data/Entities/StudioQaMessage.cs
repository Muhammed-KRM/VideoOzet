using System;

namespace VideoOzet.Data.Entities;

public class StudioQaMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EgitimId { get; set; }
    public Guid? ContentRequestId { get; set; }
    public int? BolumNo { get; set; }
    public Guid? KaynakId { get; set; }
    public string Kapsam { get; set; } = "Bolum"; // "Bolum" | "Plan" | "Kaynak"
    public string Rol { get; set; } = "user"; // "user" | "assistant"
    public string Mesaj { get; set; } = string.Empty;
    public string? OneriJson { get; set; }
    public string? KullanilanBaglamOzeti { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
}
