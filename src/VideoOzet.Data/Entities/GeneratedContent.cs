using System;

namespace VideoOzet.Data.Entities;

public class GeneratedContent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentRequestId { get; set; }
    public string ArastirmaOzeti { get; set; } = string.Empty;
    public string VideoPlani { get; set; } = string.Empty;
    public string KullanilanKaynaklar { get; set; } = "[]";
    public string LlmModel { get; set; } = string.Empty;
    public int TokenKullanimi { get; set; }
    public int UretimSuresiMs { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    public ContentRequest ContentRequest { get; set; } = null!;
}
