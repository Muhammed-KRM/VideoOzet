using System;
using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class ContentRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EgitimId { get; set; }
    public string Konu { get; set; } = string.Empty;
    public string? HedefUzunluk { get; set; }
    public string? HedefKitle { get; set; }
    public ContentRequestDurumu Durum { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? TamamlanmaTarihi { get; set; }

    public Egitim Egitim { get; set; } = null!;
    public GeneratedContent? GeneratedContent { get; set; }
    public QcResult? QcResult { get; set; }
}
