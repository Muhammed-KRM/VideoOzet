using System;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Services;

public class QcEvaluationResult
{
    public int ToplamIddiaSayisi { get; set; }
    public int DesteklenenSayisi { get; set; }
    public int BelirsizSayisi { get; set; }
    public int DesteklenmeyenSayisi { get; set; }
    public decimal GuvenSkorYuzde { get; set; }
    public string DetayliRaporJson { get; set; } = "[]";
    public BolumDurumu Durum { get; set; } = BolumDurumu.Tamamlandi;
}

public interface IQualityCheckService
{
    Task<QcEvaluationResult> EvaluateAsync(
        string contentText,
        string contextData,
        int claimCount = 8,
        string? previousReportJson = null,
        CancellationToken ct = default);
}
