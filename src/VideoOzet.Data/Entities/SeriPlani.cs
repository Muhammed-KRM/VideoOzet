using System;
using System.Collections.Generic;
using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Entities;

public class SeriPlani
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentRequestId { get; set; }
    public int PlanNo { get; set; }
    public int VideoSayisi { get; set; }
    public int VarsayilanVideoSuresiDk { get; set; }
    public string SeriHaritasiJson { get; set; } = string.Empty;
    public string DisaridaBirakilanlarJson { get; set; } = string.Empty;
    public string KullaniciKisitlariJson { get; set; } = string.Empty;
    public bool OneridenFarkli { get; set; }
    public bool Onaylandi { get; set; }
    public SeriPlanDurumu Durum { get; set; }
    
    public ContentRequest ContentRequest { get; set; } = null!;
    public ICollection<SeriBolum> SeriBolumler { get; set; } = new List<SeriBolum>();
}
