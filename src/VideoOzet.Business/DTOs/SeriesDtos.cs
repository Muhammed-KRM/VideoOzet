using System;
using System.Collections.Generic;

namespace VideoOzet.Business.DTOs;

public class CreateSeriesRequestDto
{
    public string Odak { get; set; } = string.Empty;
    public string HedefKitle { get; set; } = string.Empty;
    public string EkTonTalimati { get; set; } = string.Empty;
}

public class SeriesDraftDto
{
    public int? VideoSayisi { get; set; }
    public int? VarsayilanSureDk { get; set; }
    public Dictionary<int, int> VideoSureleri { get; set; } = new();
    public List<string> HaricKonular { get; set; } = new();
    public List<string> ZorunluKonular { get; set; } = new();
    public Dictionary<string, int> KilitliAtamalar { get; set; } = new();

    private string _talimat = string.Empty;

    public string Talimat
    {
        get => _talimat;
        set => _talimat = value ?? string.Empty;
    }

    public string RevizyonTalimati
    {
        get => _talimat;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                _talimat = value;
        }
    }
}

public class EpisodeRevisionDto
{
    public string Talimat { get; set; } = string.Empty;
    public string HedefAlan { get; set; } = "Hepsi";
}

public class ApprovePlanDto
{
    public string? Talimat { get; set; }
}

public class CourseSourceCardDto
{
    public Guid KaynakId { get; set; }
    public string KaynakTuru { get; set; } = string.Empty;
    public string Baslik { get; set; } = string.Empty;
    public string Dil { get; set; } = "tr";
    public string KullanimTuru { get; set; } = string.Empty;
    public int TahminiToplamSureDk { get; set; }
    public List<string> Konular { get; set; } = new();
    public string OzetBilgi { get; set; } = string.Empty;

    public CourseSourceCardDto()
    {
    }

    public CourseSourceCardDto(
        Guid kaynakId,
        string kaynakTuru,
        string baslik,
        string dil,
        string kullanimTuru,
        int tahminiToplamSureDk,
        List<string>? konular = null,
        string ozetBilgi = "")
    {
        KaynakId = kaynakId;
        KaynakTuru = kaynakTuru;
        Baslik = baslik;
        Dil = dil;
        KullanimTuru = kullanimTuru;
        TahminiToplamSureDk = tahminiToplamSureDk;
        Konular = konular ?? new List<string>();
        OzetBilgi = ozetBilgi;
    }
}

public class SeriesSceneOutlineItemDto
{
    public int SahneNo { get; set; }
    public string Baslik { get; set; } = string.Empty;
    public int TahminiSureDk { get; set; } = 3;
    public string AciklamaVeArgumanlar { get; set; } = string.Empty;
    public List<string> AtananKaynakKodlari { get; set; } = new();

    public SeriesSceneOutlineItemDto()
    {
    }

    public SeriesSceneOutlineItemDto(
        int sahneNo,
        string baslik,
        int tahminiSureDk,
        string aciklamaVeArgumanlar,
        List<string>? atananKaynakKodlari = null)
    {
        SahneNo = sahneNo;
        Baslik = baslik;
        TahminiSureDk = tahminiSureDk;
        AciklamaVeArgumanlar = aciklamaVeArgumanlar;
        AtananKaynakKodlari = atananKaynakKodlari ?? new List<string>();
    }
}

public class UpdatePlanDto
{
    public List<UpdateEpisodeItemDto> Bolumler { get; set; } = new();
}

public class UpdateEpisodeItemDto
{
    public int BolumNo { get; set; }
    public string CalismaBasligi { get; set; } = string.Empty;
    public string Kanca { get; set; } = string.Empty;
    public string AnaFikir { get; set; } = string.Empty;
    public int HedefSureDk { get; set; } = 10;
    public List<string> Konular { get; set; } = new();
    public List<SeriesSceneOutlineItemDto> SahneIskeleti { get; set; } = new();
    public List<Guid> AtananKaynakIdleri { get; set; } = new();
}

public class SeriesEpisodeGenerationRequest
{
    public int BolumNo { get; set; } = 1;
    public int ToplamBolum { get; set; } = 1;
    public int ToplamBolumSayisi
    {
        get => ToplamBolum;
        set => ToplamBolum = value;
    }

    public string CalismaBasligi { get; set; } = string.Empty;
    public string Kanca { get; set; } = string.Empty;
    public string AnaFikir { get; set; } = string.Empty;
    public string BolumAnaFikri
    {
        get => AnaFikir;
        set => AnaFikir = value;
    }

    public int HedefSureDk { get; set; } = 15;
    public string BolumKonulariJson { get; set; } = "[]";
    public string SahneIskeletiJson { get; set; } = "[]";
    public string YazarTeziVeGenelOdak { get; set; } = string.Empty;

    private string? _seriKonusu;
    public string SeriKonusu
    {
        get => !string.IsNullOrWhiteSpace(_seriKonusu) ? _seriKonusu : YazarTeziVeGenelOdak;
        set => _seriKonusu = value;
    }

    public string EkTonTalimati { get; set; } = string.Empty;
    public string OnayTalimati { get; set; } = string.Empty;
    public string OncekiBolumDevirDefteriJson { get; set; } = string.Empty;
    public string SonrakiBolumOzeti { get; set; } = string.Empty;
    public string HedefKitle { get; set; } = string.Empty;
    public string KaynakBaglamiText { get; set; } = string.Empty;

    public SeriesEpisodeGenerationRequest()
    {
    }

    public SeriesEpisodeGenerationRequest(
        int bolumNo,
        int toplamBolum,
        string calismaBasligi,
        string kanca,
        string anaFikir,
        int hedefSureDk,
        string bolumKonulariJson,
        string sahneIskeletiJson,
        string yazarTeziVeGenelOdak,
        string ekTonTalimati,
        string onayTalimati,
        string oncekiBolumDevirDefteriJson,
        string hedefKitle,
        string kaynakBaglamiText,
        string? seriKonusu = null)
    {
        BolumNo = bolumNo;
        ToplamBolum = toplamBolum;
        CalismaBasligi = calismaBasligi;
        Kanca = kanca;
        AnaFikir = anaFikir;
        HedefSureDk = hedefSureDk;
        BolumKonulariJson = bolumKonulariJson;
        SahneIskeletiJson = sahneIskeletiJson;
        YazarTeziVeGenelOdak = yazarTeziVeGenelOdak;
        EkTonTalimati = ekTonTalimati;
        OnayTalimati = onayTalimati;
        OncekiBolumDevirDefteriJson = oncekiBolumDevirDefteriJson;
        HedefKitle = hedefKitle;
        KaynakBaglamiText = kaynakBaglamiText;
        _seriKonusu = seriKonusu;
    }
}

public class EpisodeContinuityLedgerDto
{
    public int BolumNo { get; set; }
    public string CalismaBasligi { get; set; } = string.Empty;
    public List<string> IslenenDusunurlerVeKavramlar { get; set; } = new();
    public List<string> VerilenSomutOrnekler { get; set; } = new();
    public List<string> AcikBirakilanSorular { get; set; } = new();
    public string SonCumleGecisNotu { get; set; } = string.Empty;
}

