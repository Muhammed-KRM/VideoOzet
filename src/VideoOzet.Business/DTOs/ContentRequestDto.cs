using System;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.DTOs;

public class ContentRequestDto
{
    public Guid Id { get; set; }
    public Guid EgitimId { get; set; }
    public string Konu { get; set; } = string.Empty;
    public string? HedefUzunluk { get; set; }
    public string? HedefKitle { get; set; }
    public ContentRequestDurumu Durum { get; set; }
    public DateTime OlusturmaTarihi { get; set; }
    public DateTime? TamamlanmaTarihi { get; set; }

    public GeneratedContentDto? GeneratedContent { get; set; }
    public QcResultDto? QcResult { get; set; }
    public List<ContentVersionDto> Versions { get; set; } = new();
}

public class ContentVersionDto
{
    public Guid Id { get; set; }
    public int VersiyonNo { get; set; }
    public string ArastirmaOzeti { get; set; } = string.Empty;
    public string VideoPlani { get; set; } = string.Empty;
    public string? RevizeTalimati { get; set; }
    public string LlmModel { get; set; } = string.Empty;
    public DateTime OlusturmaTarihi { get; set; }

    // Versiyona Özel QC Bilgileri
    public int? ToplamIddiaSayisi { get; set; }
    public int? DesteklenenSayisi { get; set; }
    public int? BelirsizSayisi { get; set; }
    public int? DesteklenmeyenSayisi { get; set; }
    public string? DetayliRapor { get; set; }
    public decimal? GuvenSkorYuzde { get; set; }
    public DateTime? QcTarihi { get; set; }
}

public class ReviseContentRequestDto
{
    public string RevizeTalimati { get; set; } = string.Empty;
    public string HedefAlan { get; set; } = "hepsi"; // "hepsi", "ozet", "plan"
}

public class UsedSourceChunkDto
{
    public string KanitKodu { get; set; } = string.Empty;
    public Guid ChunkId { get; set; }
    public string KaynakTipi { get; set; } = "Dokuman";
    public string KaynakBaslik { get; set; } = string.Empty;
    public string? BolumBasligi { get; set; }
    public string? ZamanAraligi { get; set; }
    public string KaynakDil { get; set; } = "tr";
    public string KullanimTuru { get; set; } = "DogrudanReferans";
}

public class GeneratedContentDto
{
    public Guid Id { get; set; }
    public string ArastirmaOzeti { get; set; } = string.Empty;
    public string VideoPlani { get; set; } = string.Empty;
    public string KullanilanKaynaklar { get; set; } = string.Empty;
    public List<UsedSourceChunkDto> KullanilanKaynakDetaylari { get; set; } = new();
    public string LlmModel { get; set; } = string.Empty;
    public int TokenKullanimi { get; set; }
    public int UretimSuresiMs { get; set; }
    public DateTime OlusturmaTarihi { get; set; }
}

public class QcResultDto
{
    public Guid Id { get; set; }
    public int ToplamIddiaSayisi { get; set; }
    public int DesteklenenSayisi { get; set; }
    public int BelirsizSayisi { get; set; }
    public int DesteklenmeyenSayisi { get; set; }
    public string DetayliRapor { get; set; } = string.Empty;
    public decimal GuvenSkorYuzde { get; set; }
    public DateTime OlusturmaTarihi { get; set; }
}
