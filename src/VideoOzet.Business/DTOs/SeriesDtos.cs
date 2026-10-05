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
    public string Talimat { get; set; } = string.Empty;
}

public class EpisodeRevisionDto
{
    public string Talimat { get; set; } = string.Empty;
    public string HedefAlan { get; set; } = string.Empty;
}
