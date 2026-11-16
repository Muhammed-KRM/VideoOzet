using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    private readonly IDeterministicClaimScanner _claimScanner;

    public QualityCheckService(
        ISynthesisProvider synthesisProvider,
        ILogger<QualityCheckService> logger,
        IDeterministicClaimScanner? claimScanner = null)
    {
        _synthesisProvider = synthesisProvider;
        _logger = logger;
        _claimScanner = claimScanner ?? new DeterministicClaimScanner();
    }

    public Task<QcEvaluationResult> EvaluateAsync(
        string summaryText,
        string sourceContext,
        int claimCount = 8,
        string? previousReportJson = null,
        CancellationToken ct = default,
        string? videoPlanText = null)
        => EvaluateAsync(summaryText, sourceContext, authorThesisContext: null, claimCount, previousReportJson, videoPlanText, ct);

    public async Task<QcEvaluationResult> EvaluateAsync(
        string summaryText,
        string sourceContext,
        string? authorThesisContext,
        int claimCount = 8,
        string? previousReportJson = null,
        string? videoPlanText = null,
        CancellationToken ct = default)
    {
        var result = new QcEvaluationResult();

        if (string.IsNullOrWhiteSpace(summaryText) && string.IsNullOrWhiteSpace(videoPlanText))
        {
            result.ToplamIddiaSayisi = 1;
            result.BelirsizSayisi = 1;
            result.GuvenSkorYuzde = 0m;
            result.Durum = BolumDurumu.Hata;
            result.DetayliRaporJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    iddia = "Denetlenecek içerik üretilmedi.",
                    iddiaTuru = "olgusal_kaynak_iddiasi",
                    durum = "belirsiz",
                    kanitKodu = "-",
                    kaynakAlani = "ArastirmaOzeti",
                    aciklama = "İçerik metni boş olduğu için kalite kontrol yapılamadı."
                }
            });
            return result;
        }

        if (string.IsNullOrWhiteSpace(sourceContext))
        {
            result.ToplamIddiaSayisi = 1;
            result.BelirsizSayisi = 1;
            result.GuvenSkorYuzde = 0m;
            result.Durum = BolumDurumu.Hata;
            result.DetayliRaporJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    iddia = "Referans kaynak bağlamı eksik.",
                    iddiaTuru = "olgusal_kaynak_iddiasi",
                    durum = "belirsiz",
                    kanitKodu = "-",
                    kaynakAlani = "ArastirmaOzeti",
                    aciklama = "Referans kaynak bağlamı bulunamadığı için doğrulama yapılamadı."
                }
            });
            return result;
        }

        // 1. Özet + Video Planı Prompter metnini birleştir ve deterministik sayısal tarama çalıştır
        var prompterExcerpt = ExtractPrompterExcerpt(videoPlanText);
        var combinedForScan = string.IsNullOrWhiteSpace(prompterExcerpt)
            ? summaryText ?? string.Empty
            : $"{summaryText}\n\n{prompterExcerpt}";

        var anomalies = _claimScanner.FindUngroundedNumbersAndCitations(combinedForScan, sourceContext);

        var targetContentSb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(authorThesisContext))
        {
            targetContentSb.AppendLine("=== [YAZAR-TEZİ VE KAVRAMSAL ÇERÇEVE (KAYNAK ARAMASINDAN MUAF YORUM KATMANI)] ===");
            targetContentSb.AppendLine(authorThesisContext.Trim());
            targetContentSb.AppendLine("(VERİSCORE KURALI: Yukarıdaki [YAZAR-TEZİ] kapsamındaki kavramsal yorumlar, felsefi çerçeve, pedagojik benzetmeler ve sentez çıkarımları \"yazar_tezi_yorumu\" olarak sınıflandırılır; uydurma sayısal oran/yüzde, hayali deney veya sahte düşünür atfı içermediği sürece ASLA \"desteklenmedi\" sayılmaz, \"desteklendi\" ve kanitKodu=\"[YAZAR-TEZİ]\" olarak işaretlenir!)");
            targetContentSb.AppendLine();
        }

        if (anomalies.Count > 0)
        {
            targetContentSb.AppendLine("⚠️ ÖNCELİKLİ ŞÜPHELİ SAYISAL İFADELER (Kaynak metinde bulunmayan oran/yüzde — olgusal_kaynak_iddiasi olarak denetle):");
            foreach (var a in anomalies.Take(6))
            {
                targetContentSb.AppendLine($"- Şüpheli oran '{a.MatchedValue}': \"{a.Sentence}\"");
            }
            targetContentSb.AppendLine();
        }

        targetContentSb.AppendLine("=== ARAŞTIRMA ÖZETİ ===");
        targetContentSb.AppendLine(summaryText ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(prompterExcerpt))
        {
            targetContentSb.AppendLine();
            targetContentSb.AppendLine("=== VİDEO PLANI PROMPTER METNİ ===");
            targetContentSb.AppendLine(prompterExcerpt);
        }

        int effectiveClaimCount = !string.IsNullOrWhiteSpace(prompterExcerpt)
            ? Math.Max(claimCount, 12)
            : claimCount;

        string qcResponse;
        try
        {
            if (!string.IsNullOrWhiteSpace(previousReportJson))
            {
                qcResponse = await _synthesisProvider.ReQualityCheckAsync(
                    targetContentSb.ToString(),
                    previousReportJson,
                    sourceContext,
                    effectiveClaimCount,
                    ct);
            }
            else
            {
                qcResponse = await _synthesisProvider.BatchQualityCheckAsync(
                    targetContentSb.ToString(),
                    sourceContext,
                    effectiveClaimCount,
                    ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QC LLM çağrısı başarısız oldu, güven skoru 0 olarak raporlanıyor.");
            result.ToplamIddiaSayisi = 1;
            result.BelirsizSayisi = 1;
            result.GuvenSkorYuzde = 0m;
            result.Durum = BolumDurumu.Hata;
            result.DetayliRaporJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    iddia = "Kalite kontrol analizi tamamlanamadı.",
                    iddiaTuru = "olgusal_kaynak_iddiasi",
                    durum = "belirsiz",
                    kanitKodu = "-",
                    kaynakAlani = "ArastirmaOzeti",
                    aciklama = $"Kalite kontrol modeli yanıt veremedi: {ex.Message}"
                }
            });
            return result;
        }

        var raporList = new List<QcClaimReportItem>();
        int desteklenen = 0, belirsiz = 0, desteklenmeyen = 0;

        try
        {
            var jsonStr = ExtractJsonArray(qcResponse);
            if (!string.IsNullOrEmpty(jsonStr))
            {
                using var doc = JsonDocument.Parse(jsonStr, new JsonDocumentOptions { AllowTrailingCommas = true });
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    var claim = elem.TryGetProperty("iddia", out var idProp) ? (idProp.GetString() ?? "") : "";
                    var iddiaTuru = elem.TryGetProperty("iddiaTuru", out var itProp)
                        ? (itProp.GetString()?.Trim().ToLowerInvariant() ?? "olgusal_kaynak_iddiasi")
                        : "olgusal_kaynak_iddiasi";
                    var durum = elem.TryGetProperty("durum", out var dProp) ? (dProp.GetString()?.Trim().ToLowerInvariant() ?? "belirsiz") : "belirsiz";
                    var aciklama = elem.TryGetProperty("aciklama", out var aProp) ? (aProp.GetString() ?? "") : "";
                    var kanitKodu = elem.TryGetProperty("kanitKodu", out var kProp) ? (kProp.GetString() ?? "-") : "-";
                    var kaynakAlani = elem.TryGetProperty("kaynakAlani", out var kaProp) ? (kaProp.GetString() ?? "ArastirmaOzeti") : "ArastirmaOzeti";

                    if (iddiaTuru != "yazar_tezi_yorumu" && iddiaTuru != "olgusal_kaynak_iddiasi")
                    {
                        iddiaTuru = iddiaTuru.Contains("yazar", StringComparison.OrdinalIgnoreCase) ||
                                    kanitKodu.Contains("YAZAR", StringComparison.OrdinalIgnoreCase)
                            ? "yazar_tezi_yorumu"
                            : "olgusal_kaynak_iddiasi";
                    }

                    // VeriScore Deterministik Tez Koruması:
                    // Yazar tezi yorumu olarak sınıflandırılmış ve uydurma sayısal oran içermeyen bir ifade
                    // ASLA "desteklenmedi" sayılarak Auto-Refine tarafından silinmemelidir!
                    if (iddiaTuru == "yazar_tezi_yorumu" && durum == "desteklenmedi")
                    {
                        bool hasFabricatedStats = _claimScanner.FindUngroundedNumbersAndCitations(claim, sourceContext).Count > 0;
                        if (!hasFabricatedStats)
                        {
                            durum = "desteklendi";
                            if (string.IsNullOrWhiteSpace(kanitKodu) || kanitKodu == "-")
                                kanitKodu = "[YAZAR-TEZİ]";
                            aciklama = string.IsNullOrWhiteSpace(aciklama)
                                ? "Yazar tezi ve kavramsal yorum katmanı kapsamında korundu."
                                : $"[YAZAR-TEZİ Koruması] {aciklama}";
                        }
                        else
                        {
                            iddiaTuru = "olgusal_kaynak_iddiasi";
                        }
                    }

                    if (durum == "desteklendi") desteklenen++;
                    else if (durum == "desteklenmedi") desteklenmeyen++;
                    else { durum = "belirsiz"; belirsiz++; }

                    raporList.Add(new QcClaimReportItem(claim, iddiaTuru, durum, kanitKodu, kaynakAlani, aciklama));
                }
            }
            else
            {
                _logger.LogWarning("QC yanıtında geçerli JSON dizisi bulunamadı.");
            }
        }
        catch (Exception parseEx)
        {
            _logger.LogWarning(parseEx, "QC JSON parse hatası.");
        }

        // Deterministik tarayıcının bulduğu ancak LLM raporunun atladığı uydurma yüzdeleri zorunlu "desteklenmedi" olarak ekle
        if (raporList.Count > 0 && anomalies.Count > 0)
        {
            foreach (var anomaly in anomalies.Take(3))
            {
                bool alreadyFlaggedAsUnsupported = raporList.Any(r =>
                    r.Durum != "desteklendi" &&
                    (r.Iddia.Contains(anomaly.MatchedValue, StringComparison.OrdinalIgnoreCase) ||
                     r.Aciklama.Contains(anomaly.MatchedValue, StringComparison.OrdinalIgnoreCase)));

                if (!alreadyFlaggedAsUnsupported)
                {
                    raporList.Add(new QcClaimReportItem(
                        Iddia: anomaly.Sentence,
                        IddiaTuru: "olgusal_kaynak_iddiasi",
                        Durum: "desteklenmedi",
                        KanitKodu: "-",
                        KaynakAlani: !string.IsNullOrWhiteSpace(prompterExcerpt) && prompterExcerpt.Contains(anomaly.MatchedValue, StringComparison.OrdinalIgnoreCase)
                            ? "VideoPlani"
                            : "ArastirmaOzeti",
                        Aciklama: $"Deterministik sayısal denetim: '{anomaly.MatchedValue}' oranı verilen [K#] kaynak metinlerinde bulunamadı."));
                    desteklenmeyen++;
                }
            }
        }

        if (!raporList.Any())
        {
            result.ToplamIddiaSayisi = 1;
            result.DesteklenenSayisi = 0;
            result.BelirsizSayisi = 1;
            result.DesteklenmeyenSayisi = 0;
            result.GuvenSkorYuzde = 0m;
            result.Durum = BolumDurumu.Hata;
            result.DetayliRaporJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    iddia = "Kalite kontrol JSON yanıtı çözümlenemedi.",
                    iddiaTuru = "olgusal_kaynak_iddiasi",
                    durum = "belirsiz",
                    kanitKodu = "-",
                    kaynakAlani = "ArastirmaOzeti",
                    aciklama = "Model geçerli bir kalite kontrol raporu döndüremediği için doğrulama tamamlanamadı."
                }
            });
            return result;
        }

        result.ToplamIddiaSayisi = raporList.Count;
        result.DesteklenenSayisi = desteklenen;
        result.BelirsizSayisi = belirsiz;
        result.DesteklenmeyenSayisi = desteklenmeyen;
        result.GuvenSkorYuzde = Math.Round((decimal)desteklenen / raporList.Count * 100m, 2);
        result.DetayliRaporJson = JsonSerializer.Serialize(raporList.Select(r => new
        {
            iddia = r.Iddia,
            iddiaTuru = r.IddiaTuru,
            durum = r.Durum,
            kanitKodu = r.KanitKodu,
            kaynakAlani = r.KaynakAlani,
            aciklama = r.Aciklama
        }));
        result.Durum = BolumDurumu.Tamamlandi;

        return result;
    }

    private static string ExtractPrompterExcerpt(string? videoPlanText)
    {
        if (string.IsNullOrWhiteSpace(videoPlanText))
            return string.Empty;

        var quoteLines = videoPlanText.Replace("\r\n", "\n").Split('\n')
            .Where(l => l.TrimStart().StartsWith(">", StringComparison.Ordinal) ||
                        l.Contains("Prompter", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Trim().TrimStart('>').Trim())
            .Where(l => l.Length > 10)
            .ToList();

        if (quoteLines.Count > 0)
        {
            var joined = string.Join("\n", quoteLines);
            return joined.Length > 18000 ? joined[..18000] : joined;
        }

        return videoPlanText.Length > 12000 ? videoPlanText[..12000] : videoPlanText;
    }

    private static string ExtractJsonArray(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var clean = raw.Replace("```json", "", StringComparison.OrdinalIgnoreCase).Replace("```", "").Trim();
        int start = clean.IndexOf('[');
        int end = clean.LastIndexOf(']');
        if (start >= 0 && end > start)
            return clean.Substring(start, end - start + 1);
        return string.Empty;
    }

    private sealed record QcClaimReportItem(
        string Iddia,
        string IddiaTuru,
        string Durum,
        string KanitKodu,
        string KaynakAlani,
        string Aciklama);
}
