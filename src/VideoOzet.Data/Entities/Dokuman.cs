using System;
using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class Dokuman
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EgitimId { get; set; }
    public string DosyaAdi { get; set; } = string.Empty;
    public string DosyaYolu { get; set; } = string.Empty;
    public string Uzanti { get; set; } = string.Empty; // .pdf, .docx, .pptx vb.
    public long DosyaBoyutu { get; set; }
    public VideoIslemDurumu IslemDurumu { get; set; } // İşlem durumu enum'unu ortak kullanabiliriz
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? IslemTamamlanmaTarihi { get; set; }

    public Egitim Egitim { get; set; } = null!;
    public DokumanMetin? DokumanMetin { get; set; }
}
