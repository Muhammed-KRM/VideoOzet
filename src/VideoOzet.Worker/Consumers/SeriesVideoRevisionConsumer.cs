using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Services;

namespace VideoOzet.Worker.Consumers;

public class SeriesVideoRevisionConsumer : IConsumer<SeriesVideoRevisionRequestedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ISourceContextBuilder? _sourceContextBuilder;
    private readonly IQualityCheckService _qcService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SeriesVideoRevisionConsumer> _logger;

    public SeriesVideoRevisionConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        IPublishEndpoint publishEndpoint,
        ILogger<SeriesVideoRevisionConsumer> logger,
        ISourceContextBuilder? sourceContextBuilder = null,
        IQualityCheckService? qcService = null)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _sourceContextBuilder = sourceContextBuilder;
        _qcService = qcService ?? new QualityCheckService(synthesisProvider, Microsoft.Extensions.Logging.Abstractions.NullLogger<QualityCheckService>.Instance);
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SeriesVideoRevisionRequestedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("SeriesVideoRevisionRequestedEvent received for BolumId {BolumId}", msg.SeriBolumId);

        try
        {
            var bolum = await _dbContext.SeriBolumler
                .Include(b => b.Revizyonlar)
                .Include(b => b.SeriPlani)
                    .ThenInclude(p => p.ContentRequest)
                .FirstOrDefaultAsync(b => b.Id == msg.SeriBolumId, context.CancellationToken);

            if (bolum == null)
            {
                _logger.LogWarning("SeriBolum bulunamadı. ID: {BolumId}", msg.SeriBolumId);
                return;
            }

            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = msg.EgitimId,
                Asama = "Video revize ediliyor...",
                Durum = "İşleniyor",
                Mesaj = $"Bölüm {bolum.BolumNo} için kaynaklar taranıyor ve içerik güncelleniyor."
            });

            var seriPlani = bolum.SeriPlani;
            var request = seriPlani?.ContentRequest;

            // Kullanıcının ekranda seçtiği sürümü (BazRevizyonNo) veya AktifRevizyonId'yi baz al; eski sürümleri ASLA değiştirme, yeni numara aç!
            int yeniRevizyonNo = (bolum.Revizyonlar.Any() ? bolum.Revizyonlar.Max(r => r.RevizyonNo) : 0) + 1;
            BolumRevizyonu? sonRevizyon = null;
            if (msg.BazRevizyonNo.HasValue && msg.BazRevizyonNo.Value > 0)
            {
                sonRevizyon = bolum.Revizyonlar.FirstOrDefault(r => r.RevizyonNo == msg.BazRevizyonNo.Value);
            }
            if (sonRevizyon == null && bolum.AktifRevizyonId.HasValue)
            {
                sonRevizyon = bolum.Revizyonlar.FirstOrDefault(r => r.Id == bolum.AktifRevizyonId.Value);
            }
            sonRevizyon ??= bolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault();

            string currentOzet = sonRevizyon?.ArastirmaOzeti ?? "";
            string currentPlan = sonRevizyon?.VideoPlani ?? "";

            // Önceki revizyonda kullanılan chunk ID'lerini ve bölüme atanan kaynak ID'lerini ayrıştır
            List<Guid> previousChunkIds = ParseGuidList(sonRevizyon?.KullanilanKaynaklar);
            List<Guid> assignedIds = ParseGuidList(bolum.AtananKaynaklarJson);

            // Kullanıcının revizyon talimatında ismen belirttiği veya plan sonrası yeni eklediği tüm kaynakları tespit et
            var mentionedSourceIds = await FindMentionedOrLatestSourceIdsAsync(
                msg.EgitimId,
                msg.Talimat,
                request?.OlusturmaTarihi,
                context.CancellationToken);
            if (mentionedSourceIds.Count > 0)
            {
                assignedIds = mentionedSourceIds.Concat(assignedIds).Distinct().ToList();
            }

            // Bölüm konularını ve sahne iskeleti başlıklarını çıkar
            var topicsList = ParseTopicsAndSceneHints(bolum.KonularJson, bolum.SahneIskeletiJson);
            if (!topicsList.Any() && !string.IsNullOrWhiteSpace(bolum.CalismaBasligi))
            {
                topicsList.Add(bolum.CalismaBasligi);
            }

            var searchQueries = new List<string>
            {
                msg.Talimat ?? "",
                bolum.CalismaBasligi ?? "",
                bolum.AnaFikir ?? "",
                bolum.Kanca ?? ""
            };
            searchQueries.AddRange(topicsList);
            searchQueries = searchQueries
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Select(q => q.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string contextData = "";
            List<Guid> mergedChunkIds = new(previousChunkIds);

            // Eski 24 chunk'a hapsolmak yerine HER ZAMAN revizyon talimatı + bölüm başlığı + ana fikir + konular ile dinamik arama yap
            // ve önceki kullanılan chunk'larla birleştir!
            if (_sourceContextBuilder != null)
            {
                SourceContextResult? dynamicContext = null;
                try
                {
                    dynamicContext = await _sourceContextBuilder.BuildContextAsync(
                        msg.EgitimId,
                        searchQueries,
                        maxChars: 130000,
                        preferredSourceIds: assignedIds.Count > 0 ? assignedIds : null,
                        targetChunksOverride: null,
                        ct: context.CancellationToken);
                }
                catch (Exception ctxEx)
                {
                    _logger.LogWarning(ctxEx, "Dinamik kaynak bağlamı oluşturulurken uyarı oluştu, önceki chunk'lara düşülüyor.");
                }

                SourceContextResult? prevContext = null;
                if (previousChunkIds.Any())
                {
                    try
                    {
                        prevContext = await _sourceContextBuilder.BuildFromChunkIdsAsync(
                            msg.EgitimId,
                            previousChunkIds,
                            context.CancellationToken);
                    }
                    catch { }
                }

                var dynamicIds = dynamicContext?.UsedChunkIds ?? new List<Guid>();
                mergedChunkIds = dynamicIds
                    .Concat(previousChunkIds)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();

                if (mergedChunkIds.Count > dynamicIds.Count && dynamicIds.Count > 0)
                {
                    try
                    {
                        var unifiedContext = await _sourceContextBuilder.BuildFromChunkIdsAsync(
                            msg.EgitimId,
                            mergedChunkIds,
                            context.CancellationToken);

                        if (!string.IsNullOrWhiteSpace(unifiedContext.ContextText) &&
                            unifiedContext.ContextText.Length >= (dynamicContext?.ContextText?.Length ?? 0))
                        {
                            contextData = unifiedContext.ContextText;
                            if (unifiedContext.UsedChunkIds.Any())
                                mergedChunkIds = unifiedContext.UsedChunkIds;
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(contextData))
                {
                    var dynText = dynamicContext?.ContextText ?? "";
                    var prvText = prevContext?.ContextText ?? "";

                    if (!string.IsNullOrWhiteSpace(dynText) && !string.IsNullOrWhiteSpace(prvText) &&
                        !string.Equals(dynText, prvText, StringComparison.Ordinal))
                    {
                        var combined = $"{dynText}\n\n=== ÖNCEKİ ÜRETİMDE KULLANILAN EK KAYNAK KANITLARI ===\n{prvText}";
                        contextData = combined.Length > 145000 ? combined[..145000] : combined;
                    }
                    else if (!string.IsNullOrWhiteSpace(dynText))
                    {
                        contextData = dynText;
                    }
                    else
                    {
                        contextData = prvText;
                    }
                }
            }

            // Varsa bir önceki bölümün güncel DevirNotuJson bilgisini oku (bölümler arası süreklilik için)
            string oncekiBolumDevirNotu = "";
            if (bolum.BolumNo > 1)
            {
                var oncekiBolum = await _dbContext.SeriBolumler
                    .AsNoTracking()
                    .Include(b => b.Revizyonlar)
                    .FirstOrDefaultAsync(
                        b => b.SeriPlaniId == bolum.SeriPlaniId && b.BolumNo == bolum.BolumNo - 1,
                        context.CancellationToken);

                var oncekiAktifRev = oncekiBolum?.Revizyonlar
                    .OrderByDescending(r => r.Id == oncekiBolum.AktifRevizyonId)
                    .ThenByDescending(r => r.RevizyonNo)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(oncekiAktifRev?.DevirNotuJson))
                {
                    oncekiBolumDevirNotu = oncekiAktifRev.DevirNotuJson;
                }
            }

            var yazarTeziText = $"[YAZAR-TEZİ]: {request?.Konu} {request?.EkTonTalimati} {seriPlani?.OnayTalimati}".Trim();
            var enrichedTopic = $"{bolum.CalismaBasligi} (Hedef Süre: ~{(bolum.HedefSureDk > 0 ? bolum.HedefSureDk : 15)} dk | Bölüm Ana Fikri: {bolum.AnaFikir}) — {yazarTeziText}";
            if (!string.IsNullOrWhiteSpace(oncekiBolumDevirNotu))
            {
                enrichedTopic += $" | [ÖNCEKİ BÖLÜM DEVİR DEFTERİ]: {oncekiBolumDevirNotu}";
            }

            var surgicalPreservationRule =
                "CERRAHİ DÜZENLEME VE KORUMA KURALI (ÇOK ÖNEMLİ): " +
                "Kullanıcı yalnızca belirli bir sahnenin/paragrafın/kısmın değiştirilmesini veya eklenmesini istediyse, " +
                "talimatın dışında kalan TÜM diğer sahneleri, başlıkları, zaman aralıklarını ve prompter cümlelerini KELİMESİ KELİMESİNE AYNEN KORU; " +
                "değiştirilmesi istenmeyen hiçbir cümleyi silme, kısaltma veya yeniden yazma!";
            var enrichedInstruction = $"{msg.Talimat}\n\n{surgicalPreservationRule}\n\n{yazarTeziText}".Trim();

            // Revizyon işlemini AI ile gerçekleştir
            string yeniOzet = currentOzet;
            if (string.IsNullOrEmpty(msg.HedefAlan) || msg.HedefAlan == "Hepsi" || msg.HedefAlan == "ArastirmaOzeti")
            {
                yeniOzet = await _synthesisProvider.ReviseContentAsync(
                    currentOzet,
                    enrichedInstruction,
                    "Araştırma Özeti",
                    enrichedTopic,
                    contextData,
                    context.CancellationToken);
            }

            string yeniPlan = currentPlan;
            if (string.IsNullOrEmpty(msg.HedefAlan) || msg.HedefAlan == "Hepsi" || msg.HedefAlan == "VideoPlani")
            {
                yeniPlan = await _synthesisProvider.ReviseContentAsync(
                    currentPlan,
                    enrichedInstruction,
                    "Video Planı",
                    enrichedTopic,
                    contextData,
                    context.CancellationToken);
            }

            var guncelDevirNotuJson = BuildUpdatedContinuityLedgerJson(
                bolum,
                yeniOzet,
                yeniPlan,
                sonRevizyon?.DevirNotuJson,
                msg.Talimat);

            var yeniRevizyon = new BolumRevizyonu
            {
                SeriBolumId = bolum.Id,
                RevizyonNo = yeniRevizyonNo,
                Talimat = msg.Talimat,
                HedefAlan = string.IsNullOrWhiteSpace(msg.HedefAlan) ? "Hepsi" : msg.HedefAlan,
                ArastirmaOzeti = yeniOzet,
                VideoPlani = yeniPlan,
                DevirNotuJson = guncelDevirNotuJson,
                KullanilanKaynaklar = mergedChunkIds.Any()
                    ? JsonSerializer.Serialize(mergedChunkIds)
                    : (sonRevizyon?.KullanilanKaynaklar ?? "[]"),
                LlmModel = string.IsNullOrWhiteSpace(_synthesisProvider.ActiveModelName) ? "bilinmiyor" : _synthesisProvider.ActiveModelName,
                Tip = RevizyonTipi.KullaniciRevizyonu,
                Durum = BolumDurumu.Tamamlandi
            };

            // ─── Kalite Kontrol (QC) Yeniden Değerlendirme ───
            _logger.LogInformation("Revize edilen bölüm {BolumNo} (Rev {RevNo}) için Kalite Kontrol (ReQC) çalıştırılıyor...", bolum.BolumNo, yeniRevizyonNo);
            try
            {
                var prevReport = string.IsNullOrWhiteSpace(sonRevizyon?.DetayliRapor) || sonRevizyon.DetayliRapor == "[]"
                    ? null
                    : sonRevizyon.DetayliRapor;

                var authorThesisForQc = $"{yazarTeziText} | Bölüm Ana Fikri: {bolum.AnaFikir} | Revizyon Talimatı: {msg.Talimat}".Trim();

                var qcResult = await _qcService.EvaluateAsync(
                    summaryText: yeniOzet,
                    sourceContext: contextData,
                    authorThesisContext: authorThesisForQc,
                    claimCount: 8,
                    previousReportJson: prevReport,
                    videoPlanText: yeniPlan,
                    ct: context.CancellationToken);

                yeniRevizyon.GuvenSkorYuzde = qcResult.GuvenSkorYuzde;
                yeniRevizyon.ToplamIddiaSayisi = qcResult.ToplamIddiaSayisi;
                yeniRevizyon.DesteklenenSayisi = qcResult.DesteklenenSayisi;
                yeniRevizyon.BelirsizSayisi = qcResult.BelirsizSayisi;
                yeniRevizyon.DesteklenmeyenSayisi = qcResult.DesteklenmeyenSayisi;
                yeniRevizyon.DetayliRapor = qcResult.DetayliRaporJson;
                yeniRevizyon.QcDurumu = qcResult.Durum;
            }
            catch (Exception qcEx)
            {
                _logger.LogWarning(qcEx, "Bölüm {BolumNo} revizyon QC değerlendirmesinde hata oluştu.", bolum.BolumNo);
                yeniRevizyon.GuvenSkorYuzde = 0m;
                yeniRevizyon.ToplamIddiaSayisi = 1;
                yeniRevizyon.DesteklenenSayisi = 0;
                yeniRevizyon.BelirsizSayisi = 1;
                yeniRevizyon.DesteklenmeyenSayisi = 0;
                yeniRevizyon.QcDurumu = BolumDurumu.Hata;
                yeniRevizyon.DetayliRapor = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        iddia = "Kalite kontrol doğrulaması tamamlanamadı.",
                        durum = "belirsiz",
                        kanitKodu = "-",
                        kaynakAlani = "ArastirmaOzeti",
                        aciklama = $"Kalite kontrol sırasında hata oluştu: {qcEx.Message}"
                    }
                });
            }

            _dbContext.BolumRevizyonlari.Add(yeniRevizyon);

            bolum.AktifRevizyonId = yeniRevizyon.Id;
            bolum.Durum = BolumDurumu.Tamamlandi;
            bolum.BaglamEskidi = false;
            bolum.BaglamSorunlariJson = "[]";

            // Aynı plandaki b.BolumNo > bolum.BolumNo olan tamamlanmış bölümlerde BaglamEskidi = true ve BaglamSorunlariJson uyarısını kaydet
            var sonrakiTamamlanmisBolumler = await _dbContext.SeriBolumler
                .Where(b => b.SeriPlaniId == bolum.SeriPlaniId && b.BolumNo > bolum.BolumNo && b.Durum == BolumDurumu.Tamamlandi)
                .OrderBy(b => b.BolumNo)
                .ToListAsync(context.CancellationToken);

            if (sonrakiTamamlanmisBolumler.Count > 0)
            {
                var kisaTalimat = (msg.Talimat ?? "").Trim();
                if (kisaTalimat.Length > 120)
                    kisaTalimat = kisaTalimat[..120] + "...";

                var uyariMesaji = $"Bölüm {bolum.BolumNo} ({bolum.CalismaBasligi}) Revizyon #{yeniRevizyonNo} ile güncellendi (Talimat: \"{kisaTalimat}\"). Önceki bölümün devir bağlamı değiştiği için bu bölümü yeni bağlama göre güncelleyebilirsiniz.";

                foreach (var sonraki in sonrakiTamamlanmisBolumler)
                {
                    sonraki.BaglamEskidi = true;
                    sonraki.BaglamSorunlariJson = AppendBaglamSorunu(sonraki.BaglamSorunlariJson, uyariMesaji);
                }
            }

            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = msg.EgitimId,
                Asama = "Revizyon Tamamlandı",
                Durum = "Başarılı",
                Mesaj = "Video başarıyla güncellendi."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seri video revizyonunda hata. BolumId: {BolumId}", msg.SeriBolumId);

            // Hata yolu ASLA fırlatmamalı (aksi halde MassTransit LLM revizyonunu tekrar tekrar çalıştırır).
            await ConsumerFailureGuard.TryPersistFailureStateAsync(_dbContext, _logger, async (db, ct) =>
            {
                var bolum = await db.SeriBolumler.FirstOrDefaultAsync(b => b.Id == msg.SeriBolumId, ct);
                if (bolum != null)
                {
                    bolum.Durum = BolumDurumu.Hata;
                }
            });

            await ConsumerFailureGuard.TryRunAsync(_logger, "Publish PipelineProgressEvent",
                () => _publishEndpoint.Publish(new PipelineProgressEvent
                {
                    VideoId = Guid.Empty,
                    EgitimId = msg.EgitimId,
                    Asama = "Hata",
                    Durum = "Hata",
                    Mesaj = $"Revizyon hatası: {ex.Message}"
                }));
        }
    }

    private static string BuildUpdatedContinuityLedgerJson(
        SeriBolum bolum,
        string yeniOzet,
        string yeniPlan,
        string? oncekiDevirNotuJson,
        string? revizyonTalimati)
    {
        EpisodeContinuityLedgerDto? existingLedger = null;
        if (!string.IsNullOrWhiteSpace(oncekiDevirNotuJson) && oncekiDevirNotuJson.TrimStart().StartsWith("{"))
        {
            try
            {
                existingLedger = JsonSerializer.Deserialize<EpisodeContinuityLedgerDto>(oncekiDevirNotuJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch { }
        }

        var kavramlar = new List<string>();
        kavramlar.AddRange(ParseTopicsAndSceneHints(bolum.KonularJson, null));

        // Yeni özet ve plandaki Markdown başlıklarından işlenen kavramları/düşünürleri çıkar
        foreach (var text in new[] { yeniOzet, yeniPlan })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            foreach (Match m in Regex.Matches(text, @"^#{2,4}\s+(.+)$", RegexOptions.Multiline))
            {
                var heading = m.Groups[1].Value.Trim().Trim('*').Trim();
                if (!string.IsNullOrWhiteSpace(heading) &&
                    !heading.StartsWith("Sahne", StringComparison.OrdinalIgnoreCase) &&
                    !heading.Contains("Prompter", StringComparison.OrdinalIgnoreCase) &&
                    !heading.Contains("Görsel", StringComparison.OrdinalIgnoreCase) &&
                    heading.Length <= 110)
                {
                    kavramlar.Add(heading);
                }
            }
        }

        if (existingLedger?.IslenenDusunurlerVeKavramlar != null)
        {
            kavramlar.AddRange(existingLedger.IslenenDusunurlerVeKavramlar);
        }

        var ornekler = new List<string>();
        if (existingLedger?.VerilenSomutOrnekler != null)
        {
            ornekler.AddRange(existingLedger.VerilenSomutOrnekler);
        }
        if (!string.IsNullOrWhiteSpace(revizyonTalimati))
        {
            var trimmedRev = revizyonTalimati.Trim();
            ornekler.Add($"Revizyon vurgusu: {(trimmedRev.Length > 140 ? trimmedRev[..140] + "..." : trimmedRev)}");
        }

        var sorular = new List<string>();
        if (!string.IsNullOrWhiteSpace(yeniPlan))
        {
            var questionMatches = Regex.Matches(yeniPlan, @"([^.!?\r\n]{15,180}\?)");
            foreach (Match qm in questionMatches.TakeLast(3))
            {
                var q = qm.Groups[1].Value.Trim().TrimStart('>', '-', '*', ' ');
                if (!string.IsNullOrWhiteSpace(q))
                    sorular.Add(q);
            }
        }
        if (!sorular.Any() && existingLedger?.AcikBirakilanSorular != null)
        {
            sorular.AddRange(existingLedger.AcikBirakilanSorular);
        }

        string sonCumle = existingLedger?.SonCumleGecisNotu ?? "";
        var sourceForEnding = !string.IsNullOrWhiteSpace(yeniPlan) ? yeniPlan : yeniOzet;
        if (!string.IsNullOrWhiteSpace(sourceForEnding))
        {
            var lastLines = sourceForEnding
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim().TrimStart('>', '*', '-', ' '))
                .Where(l => l.Length > 25 &&
                            !l.StartsWith("#") &&
                            !l.StartsWith("[") &&
                            !l.StartsWith("Kamera:", StringComparison.OrdinalIgnoreCase) &&
                            !l.StartsWith("B-Roll:", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (lastLines.Count > 0)
            {
                var candidate = lastLines[^1];
                sonCumle = candidate.Length > 240 ? candidate[..240] + "..." : candidate;
            }
        }

        var updatedLedger = new EpisodeContinuityLedgerDto
        {
            BolumNo = bolum.BolumNo,
            CalismaBasligi = bolum.CalismaBasligi,
            IslenenDusunurlerVeKavramlar = kavramlar
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList(),
            VerilenSomutOrnekler = ornekler
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList(),
            AcikBirakilanSorular = sorular
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList(),
            SonCumleGecisNotu = sonCumle
        };

        return JsonSerializer.Serialize(updatedLedger);
    }

    private static string AppendBaglamSorunu(string? existingJson, string newWarning)
    {
        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                if (existingJson.TrimStart().StartsWith("["))
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(existingJson);
                    if (parsed != null)
                        warnings.AddRange(parsed.Where(s => !string.IsNullOrWhiteSpace(s)));
                }
                else
                {
                    warnings.Add(existingJson.Trim());
                }
            }
            catch
            {
                warnings.Add(existingJson.Trim());
            }
        }

        if (!warnings.Contains(newWarning, StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add(newWarning);
        }

        return JsonSerializer.Serialize(warnings.TakeLast(5).ToList());
    }

    private static List<Guid> ParseGuidList(string? raw)
    {
        var list = new List<Guid>();
        if (string.IsNullOrWhiteSpace(raw))
            return list;

        var trimmed = raw.Trim();
        try
        {
            if (trimmed.StartsWith("["))
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.ValueKind == JsonValueKind.String && Guid.TryParse(elem.GetString(), out var g) && g != Guid.Empty)
                        {
                            list.Add(g);
                        }
                        else if (elem.ValueKind == JsonValueKind.Object)
                        {
                            var idStr = elem.TryGetProperty("kaynakId", out var p) ? p.GetString()
                                : (elem.TryGetProperty("KaynakId", out p) ? p.GetString()
                                : (elem.TryGetProperty("id", out p) ? p.GetString()
                                : (elem.TryGetProperty("Id", out p) ? p.GetString() : null)));
                            if (Guid.TryParse(idStr, out var objGuid) && objGuid != Guid.Empty)
                                list.Add(objGuid);
                        }
                    }
                }
            }
            else
            {
                foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Guid.TryParse(part.Trim(), out var g) && g != Guid.Empty)
                        list.Add(g);
                }
            }
        }
        catch { }

        return list.Distinct().ToList();
    }

    private static List<string> ParseTopicsAndSceneHints(string? konularJson, string? sahneIskeletiJson)
    {
        var topicsList = new List<string>();
        if (!string.IsNullOrWhiteSpace(konularJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(konularJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.ValueKind == JsonValueKind.String)
                        {
                            var s = elem.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) topicsList.Add(s.Trim());
                        }
                        else if (elem.ValueKind == JsonValueKind.Object)
                        {
                            var b = elem.TryGetProperty("Baslik", out var bp) ? bp.GetString() : (elem.TryGetProperty("baslik", out bp) ? bp.GetString() : "");
                            var a = elem.TryGetProperty("Aciklama", out var ap) ? ap.GetString() : (elem.TryGetProperty("aciklama", out ap) ? ap.GetString() : "");
                            var combined = $"{b} {a}".Trim();
                            if (!string.IsNullOrWhiteSpace(combined)) topicsList.Add(combined);
                        }
                    }
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(sahneIskeletiJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(sahneIskeletiJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.ValueKind == JsonValueKind.Object)
                        {
                            var baslik = elem.TryGetProperty("Baslik", out var bp) ? bp.GetString()
                                : (elem.TryGetProperty("baslik", out bp) ? bp.GetString() : "");
                            var aciklama = elem.TryGetProperty("AciklamaVeArgumanlar", out var ap) ? ap.GetString()
                                : (elem.TryGetProperty("aciklamaVeArgumanlar", out ap) ? ap.GetString() : "");
                            var combined = $"{baslik} {aciklama}".Trim();
                            if (!string.IsNullOrWhiteSpace(combined))
                                topicsList.Add(combined);
                        }
                    }
                }
            }
            catch { }
        }

        return topicsList;
    }

    private async Task<List<Guid>> FindMentionedOrLatestSourceIdsAsync(
        Guid egitimId,
        string? talimat,
        DateTime? planCreatedAtUtc,
        System.Threading.CancellationToken ct)
    {
        var found = new List<Guid>();
        if (string.IsNullOrWhiteSpace(talimat) || egitimId == Guid.Empty)
            return found;

        static string Norm(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var lower = s.ToLowerInvariant()
                .Replace('ı', 'i').Replace('İ', 'i').Replace('ş', 's').Replace('ğ', 'g')
                .Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c').Replace('â', 'a').Replace('î', 'i').Replace('û', 'u');
            return System.Text.RegularExpressions.Regex.Replace(lower, @"[^a-z0-9]+", " ").Trim();
        }

        var normQuery = Norm(talimat);
        bool asksLatest = normQuery.Contains("yeni ekle") || normQuery.Contains("son ekle") ||
                          normQuery.Contains("yeni yukle") || normQuery.Contains("son yukle") ||
                          normQuery.Contains("yeni kaynak") || normQuery.Contains("yeni dosya") ||
                          normQuery.Contains("yeni video") || normQuery.Contains("yeni dokuman") ||
                          normQuery.Contains("ekledigim kaynak") || normQuery.Contains("atanmayan kaynak");

        var docs = await _dbContext.Dokumanlar.AsNoTracking()
            .Where(d => d.EgitimId == egitimId)
            .Select(d => new { d.Id, Title = d.DosyaAdi, d.OlusturmaTarihi })
            .ToListAsync(ct);

        var vids = await _dbContext.Videolar.AsNoTracking()
            .Where(v => v.EgitimId == egitimId)
            .Select(v => new { v.Id, Title = v.Baslik, v.OlusturmaTarihi })
            .ToListAsync(ct);

        var stopWords = new HashSet<string>
        {
            "video", "dokuman", "makale", "bolum", "ders", "kitap", "risalesi", "uzerine",
            "ozgur", "irade", "free", "will", "and", "the", "for", "from", "with", "prof"
        };

        var scored = new List<(Guid Id, DateTime Created, int Score)>();
        foreach (var item in docs.Concat(vids))
        {
            var stripped = System.Text.RegularExpressions.Regex.Replace(item.Title ?? "", @"\.(pdf|docx|txt|mp4|mkv|mp3|wav)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var normTitle = Norm(stripped);
            if (normTitle.Length < 4) continue;

            if (normQuery.Contains(normTitle))
            {
                scored.Add((item.Id, item.OlusturmaTarihi, 200));
                continue;
            }

            var tokens = normTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 4 && !int.TryParse(t, out _) && !stopWords.Contains(t))
                .Distinct()
                .ToList();

            int matched = 0, longest = 0;
            foreach (var tok in tokens)
            {
                if (normQuery.Contains(tok))
                {
                    matched++;
                    if (tok.Length > longest) longest = tok.Length;
                }
            }

            if (matched >= 2)
                scored.Add((item.Id, item.OlusturmaTarihi, matched * 35 + longest));
            else if (matched == 1 && longest >= 6)
                scored.Add((item.Id, item.OlusturmaTarihi, 25 + longest));
        }

        if (asksLatest && planCreatedAtUtc.HasValue)
        {
            var addedAfterPlan = docs.Concat(vids)
                .Where(x => x.OlusturmaTarihi > planCreatedAtUtc.Value)
                .OrderByDescending(x => x.OlusturmaTarihi)
                .Take(8)
                .Select(x => x.Id)
                .ToList();

            if (addedAfterPlan.Count > 0)
            {
                return scored.Select(s => s.Id).Concat(addedAfterPlan).Distinct().Take(8).ToList();
            }
        }

        if (scored.Count == 0 && asksLatest)
        {
            return docs.Concat(vids).OrderByDescending(x => x.OlusturmaTarihi).Take(5).Select(x => x.Id).ToList();
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Created)
            .Take(8)
            .Select(x => x.Id)
            .ToList();
    }
}
