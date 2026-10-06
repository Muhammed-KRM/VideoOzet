using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Services;

public class QualityCheckService : IQualityCheckService
{
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ILogger<QualityCheckService> _logger;

    public QualityCheckService(
        ISynthesisProvider synthesisProvider,
        ILogger<QualityCheckService> logger)
    {
        _synthesisProvider = synthesisProvider;
        _logger = logger;
    }

    public async Task<QcEvaluationResult> EvaluateAsync(
        string contentText,
        string contextData,
        int claimCount = 8,
        string? previousReportJson = null,
        CancellationToken ct = default)
    {
        var result = new QcEvaluationResult();

        if (string.IsNullOrWhiteSpace(contentText))
        {
            _logger.LogWarning("QC değerlendirmesi için içerik metni boş.");
            result.Durum = BolumDurumu.Hata;
            return result;
        }

        if (string.IsNullOrWhiteSpace(contextData))
        {
            _logger.LogWarning("QC değerlendirmesi için referans kaynak bağlamı boş.");
            result.ToplamIddiaSayisi = 1;
            result.BelirsizSayisi = 1;
            result.GuvenSkorYuzde = 0;
            result.DetayliRaporJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    iddia = "İçerik doğrulaması için kaynak bağlamı",
                    durum = "belirsiz",
                    aciklama = "Doğrulama yapılacak referans kaynak metinleri bulunamadığı için iddialar teyit edilemedi."
                }
            });
            result.Durum = BolumDurumu.Tamamlandi;
            return result;
        }

        try
        {
            string qcResponse;
            if (!string.IsNullOrWhiteSpace(previousReportJson) && previousReportJson != "[]")
            {
                qcResponse = await _synthesisProvider.ReQualityCheckAsync(
                    contentText,
                    previousReportJson,
                    contextData,
                    claimCount,
                    ct);
            }
            else
            {
                qcResponse = await _synthesisProvider.BatchQualityCheckAsync(
                    contentText,
                    contextData,
                    claimCount,
                    ct);
            }

            var cleanJson = CleanJsonString(qcResponse);
            var raporList = new List<object>();
            int desteklenen = 0;
            int belirsiz = 0;
            int desteklenmeyen = 0;

            try
            {
                using var doc = JsonDocument.Parse(cleanJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        var iddia = elem.TryGetProperty("iddia", out var ip) ? ip.GetString() : "İddia";
                        var durumRaw = elem.TryGetProperty("durum", out var dp) ? dp.GetString()?.ToLowerInvariant() ?? "belirsiz" : "belirsiz";
                        var aciklama = elem.TryGetProperty("aciklama", out var ap) ? ap.GetString() : "";

                        string normDurum;
                        if (durumRaw.Contains("desteklenmedi"))
                        {
                            normDurum = "desteklenmedi";
                            desteklenmeyen++;
                        }
                        else if (durumRaw.Contains("desteklendi"))
                        {
                            normDurum = "desteklendi";
                            desteklenen++;
                        }
                        else
                        {
                            normDurum = "belirsiz";
                            belirsiz++;
                        }

                        raporList.Add(new
                        {
                            iddia = iddia ?? "",
                            durum = normDurum,
                            aciklama = aciklama ?? ""
                        });
                    }
                }
            }
            catch (Exception parseEx)
            {
                _logger.LogWarning(parseEx, "QC JSON parse hatası. Ham yanıt: {QcResponse}", qcResponse);
            }

            if (!raporList.Any())
            {
                _logger.LogWarning("QC raporu boş döndü veya geçerli JSON dizisi değildi.");
                result.ToplamIddiaSayisi = 1;
                result.BelirsizSayisi = 1;
                result.GuvenSkorYuzde = 0;
                result.DetayliRaporJson = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        iddia = "İçerik analizi tamamlandı",
                        durum = "belirsiz",
                        aciklama = "Yapay zeka yanıtı yapılandırılmış formata dönüştürülemedi."
                    }
                });
                result.Durum = BolumDurumu.Tamamlandi;
                return result;
            }

            var toplam = raporList.Count;
            decimal skor = toplam == 0 ? 0 : Math.Round((decimal)desteklenen / toplam * 100, 2);

            result.ToplamIddiaSayisi = toplam;
            result.DesteklenenSayisi = desteklenen;
            result.BelirsizSayisi = belirsiz;
            result.DesteklenmeyenSayisi = desteklenmeyen;
            result.GuvenSkorYuzde = skor;
            result.DetayliRaporJson = JsonSerializer.Serialize(raporList);
            result.Durum = BolumDurumu.Tamamlandi;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QC değerlendirme sırasında hata: {Message}", ex.Message);
            result.Durum = BolumDurumu.Hata;
            return result;
        }
    }

    private static string CleanJsonString(string text)
    {
        var clean = (text ?? "").Trim();
        if (clean.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(7);
        }
        else if (clean.StartsWith("```"))
        {
            clean = clean.Substring(3);
        }
        if (clean.EndsWith("```"))
        {
            clean = clean.Substring(0, clean.Length - 3);
        }
        return clean.Trim();
    }
}
