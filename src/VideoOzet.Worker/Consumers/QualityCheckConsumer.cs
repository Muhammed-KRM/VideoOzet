using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Worker.Consumers;

public class QualityCheckConsumer : IConsumer<ContentGeneratedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ISourceContextBuilder? _sourceContextBuilder;
    private readonly IQualityCheckService? _qcService;
    private readonly ILogService _logService;
    private readonly ILogger<QualityCheckConsumer> _logger;

    public QualityCheckConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        ILogService logService,
        ILogger<QualityCheckConsumer> logger,
        ISourceContextBuilder? sourceContextBuilder = null,
        IQualityCheckService? qcService = null)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _logService = logService;
        _logger = logger;
        _sourceContextBuilder = sourceContextBuilder;
        _qcService = qcService;
    }

    public async Task Consume(ConsumeContext<ContentGeneratedEvent> context)
    {
        var message = context.Message;
        var logId = await _logService.LogPipelineStartAsync(null, message.EgitimId, PipelineAsamasi.KaliteKontrol, context.CorrelationId?.ToString());

        try
        {
            var request = await _dbContext.ContentRequests
                .Include(r => r.GeneratedContent)
                .FirstOrDefaultAsync(r => r.Id == message.ContentRequestId);

            if (request == null || request.GeneratedContent == null)
            {
                throw new Exception("ContentRequest veya GeneratedContent bulunamadı.");
            }

            request.Durum = ContentRequestDurumu.QcYapiliyor;
            await _dbContext.SaveChangesAsync();

            // 1. Kullanılan Kaynakları Getir
            var chunkIdsStr = request.GeneratedContent.KullanilanKaynaklar;
            var contextData = string.Empty;
            List<Guid> chunkIds = new();

            if (!string.IsNullOrEmpty(chunkIdsStr))
            {
                try
                {
                    if (chunkIdsStr.TrimStart().StartsWith("["))
                    {
                        chunkIds = JsonSerializer.Deserialize<List<Guid>>(chunkIdsStr) ?? new();
                    }
                    else
                    {
                        chunkIds = chunkIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, "Kullanılan kaynaklar parse edilemedi: {ChunkIdsStr}", chunkIdsStr);
                }
            }

            if (_sourceContextBuilder != null)
            {
                var scResult = await _sourceContextBuilder.BuildFromChunkIdsAsync(message.EgitimId, chunkIds, context.CancellationToken);
                contextData = scResult.ContextText;
            }
            else if (chunkIds.Any())
            {
                var chunks = await _dbContext.VideoChunkDocuments.Where(c => chunkIds.Contains(c.Id)).ToListAsync();
                var contextBuilder = new StringBuilder();
                foreach (var chunk in chunks)
                {
                    contextBuilder.AppendLine($"[VideoId={chunk.VideoId}, Zaman={chunk.StartTimeMs / 1000.0}-{chunk.EndTimeMs / 1000.0}s]");
                    contextBuilder.AppendLine(chunk.Text);
                }
                contextData = contextBuilder.ToString();
            }

            // 2. VeriScore İki Katmanlı Toplu Kalite Kontrol (QC)
            _logger.LogInformation("ContentRequest {RequestId} için VeriScore Kalite Kontrol (QC) başlatılıyor...", request.Id);

            var authorThesisContext = BuildAuthorThesisContext(request.Konu, request.EkTonTalimati);

            QcResult qcResult;
            if (_qcService != null)
            {
                var qcEval = await _qcService.EvaluateAsync(
                    request.GeneratedContent.ArastirmaOzeti,
                    contextData,
                    authorThesisContext,
                    claimCount: 12,
                    previousReportJson: null,
                    videoPlanText: request.GeneratedContent.VideoPlani,
                    ct: context.CancellationToken);

                if (qcEval == null)
                {
                    qcEval = await _qcService.EvaluateAsync(
                        request.GeneratedContent.ArastirmaOzeti,
                        contextData,
                        claimCount: 12,
                        previousReportJson: null,
                        ct: context.CancellationToken,
                        videoPlanText: request.GeneratedContent.VideoPlani);
                }

                // 1-Turlu Tez-Korumalı Otomatik Öz-Düzeltme (Auto-Refine): Yalnızca olgusal kaynak dışı uydurmaları temizle!
                var unsupportedFactualList = ExtractUnsupportedFactualDescriptions(qcEval.DetayliRaporJson);
                if (qcEval.DesteklenmeyenSayisi > 0 && !string.IsNullOrWhiteSpace(contextData) && !string.IsNullOrWhiteSpace(unsupportedFactualList))
                {
                    _logger.LogInformation(
                        "ContentRequest {RequestId} için {Unsupported} desteklenmeyen olgusal iddia tespit edildi; Tez-Korumalı Auto-Refine başlatılıyor...",
                        request.Id, qcEval.DesteklenmeyenSayisi);

                    var llmModelName = string.IsNullOrWhiteSpace(request.GeneratedContent.LlmModel)
                        ? _synthesisProvider.ActiveModelName
                        : request.GeneratedContent.LlmModel;

                    if (_dbContext.ContentVersions != null)
                    {
                        _dbContext.ContentVersions.Add(new ContentVersion
                        {
                            ContentRequestId = request.Id,
                            VersiyonNo = 1,
                            ArastirmaOzeti = request.GeneratedContent.ArastirmaOzeti,
                            VideoPlani = request.GeneratedContent.VideoPlani,
                            RevizeTalimati = "İlk Üretim (Tez-Korumalı Öz-Düzeltme Öncesi)",
                            LlmModel = llmModelName,
                            ToplamIddiaSayisi = qcEval.ToplamIddiaSayisi,
                            DesteklenenSayisi = qcEval.DesteklenenSayisi,
                            BelirsizSayisi = qcEval.BelirsizSayisi,
                            DesteklenmeyenSayisi = qcEval.DesteklenmeyenSayisi,
                            DetayliRapor = qcEval.DetayliRaporJson,
                            GuvenSkorYuzde = qcEval.GuvenSkorYuzde,
                            QcTarihi = DateTime.UtcNow
                        });
                    }

                    var refineInstruction =
                        "VERİSCORE TEZ-KORUMALI ÖZ-DÜZELTME TALİMATI:\n" +
                        "1. Kullanıcının [YAZAR-TEZİ] kapsamındaki kavramsal çerçevesini, felsefi/analitik yorumlarını ve pedagojik sentezlerini KESİNLİKLE SİLME veya KISALTMA!\n" +
                        "2. Yalnızca aşağıda listelenen, verilen [K#] kaynaklarında doğrulanamayan uydurma sayısal oranları (%), hayali deney/anketleri veya yanlış düşünür atıflarını metinden çıkar ya da [K#] kaynaklarındaki gerçek verilerle düzelt:\n" +
                        unsupportedFactualList;

                    var topicWithThesis = $"{request.Konu}\n[YAZAR-TEZİ]: {authorThesisContext}";

                    try
                    {
                        var revisedSummary = await _synthesisProvider.ReviseContentAsync(
                            request.GeneratedContent.ArastirmaOzeti,
                            refineInstruction,
                            "Araştırma Özeti",
                            topicWithThesis,
                            contextData,
                            context.CancellationToken);

                        if (!string.IsNullOrWhiteSpace(revisedSummary))
                        {
                            request.GeneratedContent.ArastirmaOzeti = revisedSummary;
                        }

                        var revisedPlan = await _synthesisProvider.ReviseContentAsync(
                            request.GeneratedContent.VideoPlani,
                            refineInstruction,
                            "Video Planı",
                            topicWithThesis,
                            contextData,
                            context.CancellationToken);

                        if (!string.IsNullOrWhiteSpace(revisedPlan))
                        {
                            request.GeneratedContent.VideoPlani = revisedPlan;
                        }

                        qcEval = await _qcService.EvaluateAsync(
                            request.GeneratedContent.ArastirmaOzeti,
                            contextData,
                            authorThesisContext,
                            claimCount: 12,
                            previousReportJson: qcEval.DetayliRaporJson,
                            videoPlanText: request.GeneratedContent.VideoPlani,
                            ct: context.CancellationToken)
                            ?? await _qcService.EvaluateAsync(
                                request.GeneratedContent.ArastirmaOzeti,
                                contextData,
                                claimCount: 12,
                                previousReportJson: qcEval.DetayliRaporJson,
                                ct: context.CancellationToken,
                                videoPlanText: request.GeneratedContent.VideoPlani);

                        if (_dbContext.ContentVersions != null)
                        {
                            _dbContext.ContentVersions.Add(new ContentVersion
                            {
                                ContentRequestId = request.Id,
                                VersiyonNo = 2,
                                ArastirmaOzeti = request.GeneratedContent.ArastirmaOzeti,
                                VideoPlani = request.GeneratedContent.VideoPlani,
                                RevizeTalimati = "Otomatik Kanıt Öz-Düzeltmesi (Tez-Korumalı Auto-Refine)",
                                LlmModel = llmModelName,
                                ToplamIddiaSayisi = qcEval.ToplamIddiaSayisi,
                                DesteklenenSayisi = qcEval.DesteklenenSayisi,
                                BelirsizSayisi = qcEval.BelirsizSayisi,
                                DesteklenmeyenSayisi = qcEval.DesteklenmeyenSayisi,
                                DetayliRapor = qcEval.DetayliRaporJson,
                                GuvenSkorYuzde = qcEval.GuvenSkorYuzde,
                                QcTarihi = DateTime.UtcNow
                            });
                        }
                    }
                    catch (Exception refineEx)
                    {
                        _logger.LogWarning(refineEx, "Auto-Refine sırasında hata oluştu, ilk QC sonucu korunuyor.");
                    }
                }

                qcResult = new QcResult
                {
                    ContentRequestId = request.Id,
                    ToplamIddiaSayisi = qcEval.ToplamIddiaSayisi,
                    DesteklenenSayisi = qcEval.DesteklenenSayisi,
                    BelirsizSayisi = qcEval.BelirsizSayisi,
                    DesteklenmeyenSayisi = qcEval.DesteklenmeyenSayisi,
                    DetayliRapor = qcEval.DetayliRaporJson,
                    GuvenSkorYuzde = qcEval.GuvenSkorYuzde,
                    OlusturmaTarihi = DateTime.UtcNow
                };
            }
            else
            {
                var summaryWithThesis = string.IsNullOrWhiteSpace(authorThesisContext)
                    ? request.GeneratedContent.ArastirmaOzeti
                    : $"[YAZAR-TEZİ]: {authorThesisContext}\n\n{request.GeneratedContent.ArastirmaOzeti}";

                var batchQcResponse = await _synthesisProvider.BatchQualityCheckAsync(
                    summaryWithThesis,
                    contextData,
                    claimCount: 8,
                    context.CancellationToken);

                var cleanJson = batchQcResponse.Trim();
                if (cleanJson.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                {
                    cleanJson = cleanJson.Substring(7);
                }
                else if (cleanJson.StartsWith("```"))
                {
                    cleanJson = cleanJson.Substring(3);
                }
                if (cleanJson.EndsWith("```"))
                {
                    cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
                }
                cleanJson = cleanJson.Trim();

                var desteklenen = 0;
                var belirsiz = 0;
                var desteklenmeyen = 0;
                var raporList = new List<object>();

                try
                {
                    using var doc = JsonDocument.Parse(cleanJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            var iddia = element.TryGetProperty("iddia", out var iddiaProp) ? iddiaProp.GetString() : "Bilinmeyen İddia";
                            var iddiaTuru = element.TryGetProperty("iddiaTuru", out var itProp) ? (itProp.GetString()?.ToLowerInvariant() ?? "olgusal_kaynak_iddiasi") : "olgusal_kaynak_iddiasi";
                            var durum = element.TryGetProperty("durum", out var durumProp) ? durumProp.GetString()?.ToLowerInvariant() ?? "belirsiz" : "belirsiz";
                            var aciklama = element.TryGetProperty("aciklama", out var aciklamaProp) ? aciklamaProp.GetString() : "";

                            if (iddiaTuru == "yazar_tezi_yorumu" && durum == "desteklenmedi")
                            {
                                durum = "desteklendi";
                            }

                            if (durum == "desteklendi") desteklenen++;
                            else if (durum == "desteklenmedi") desteklenmeyen++;
                            else { durum = "belirsiz"; belirsiz++; }

                            raporList.Add(new
                            {
                                iddia = iddia ?? "",
                                iddiaTuru = iddiaTuru,
                                durum = durum,
                                aciklama = aciklama ?? ""
                            });
                        }
                    }
                    else
                    {
                        _logger.LogWarning("QC çıktısı JSON dizisi değildi: {BatchQcResponse}", batchQcResponse);
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, "Toplu QC JSON parse hatası. Ham yanıt: {BatchQcResponse}", batchQcResponse);
                }

                if (!raporList.Any())
                {
                    belirsiz = 1;
                    raporList.Add(new
                    {
                        iddia = "Kalite kontrol JSON yanıtı çözümlenemedi.",
                        iddiaTuru = "olgusal_kaynak_iddiasi",
                        durum = "belirsiz",
                        kanitKodu = "-",
                        kaynakAlani = "ArastirmaOzeti",
                        aciklama = "Model geçerli bir kalite kontrol raporu döndüremediği için doğrulama tamamlanamadı."
                    });
                }

                var toplamIddia = raporList.Count;
                decimal guvenYuzde = toplamIddia == 0 ? 0m : Math.Round((decimal)desteklenen / toplamIddia * 100m, 2);

                qcResult = new QcResult
                {
                    ContentRequestId = request.Id,
                    ToplamIddiaSayisi = toplamIddia,
                    DesteklenenSayisi = desteklenen,
                    BelirsizSayisi = belirsiz,
                    DesteklenmeyenSayisi = desteklenmeyen,
                    DetayliRapor = JsonSerializer.Serialize(raporList),
                    GuvenSkorYuzde = guvenYuzde,
                    OlusturmaTarihi = DateTime.UtcNow
                };
            }

            _dbContext.QcResults.Add(qcResult);

            // 5. Durumu Tamamla
            request.Durum = ContentRequestDurumu.Tamamlandi;
            request.TamamlanmaTarihi = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // 6. SignalR Bildirimi için ContentProgress ve ContentReadyEvent fırlat
            await context.Publish(new ContentProgressEvent
            {
                ContentRequestId = request.Id,
                EgitimId = message.EgitimId,
                Asama = "Tamamlandı",
                Durum = "Tamamlandı",
                Yuzde = 100
            });

            await context.Publish(new ContentReadyEvent
            {
                ContentRequestId = request.Id,
                EgitimId = message.EgitimId
            });

            await _logService.LogPipelineEndAsync(logId, $"{{ \"status\": \"Success\", \"guvenSkor\": {qcResult.GuvenSkorYuzde.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} }}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QC (Kalite Kontrol) sırasında hata oluştu: {Message}", ex.Message);
            try { _dbContext?.ChangeTracker?.Clear(); } catch { }

            try
            {
                await _logService.LogFunctionErrorAsync(nameof(QualityCheckConsumer), ex, message);
                await _logService.LogPipelineErrorAsync(logId, ex);
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "Loglama servisi hatası: {Message}", logEx.Message);
            }

            try
            {
                var request = await _dbContext.ContentRequests.FindAsync(message.ContentRequestId);
                if (request != null)
                {
                    request.Durum = ContentRequestDurumu.Hata;
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "ContentRequest durum güncelleme hatası: {Message}", dbEx.Message);
            }

            try
            {
                await context.Publish(new ContentErrorEvent
                {
                    ContentRequestId = message.ContentRequestId,
                    EgitimId = message.EgitimId,
                    HataMesaji = ex.Message
                });
            }
            catch (Exception pubEx)
            {
                _logger.LogError(pubEx, "ContentErrorEvent publish hatası: {Message}", pubEx.Message);
            }
        }
    }

    private static string BuildAuthorThesisContext(string? konu, string? ekTonTalimati)
    {
        if (string.IsNullOrWhiteSpace(konu) && string.IsNullOrWhiteSpace(ekTonTalimati))
            return string.Empty;

        return string.IsNullOrWhiteSpace(ekTonTalimati)
            ? (konu ?? string.Empty).Trim()
            : $"{(konu ?? string.Empty).Trim()}\nEk Ton ve Perspektif Direktifi: {ekTonTalimati.Trim()}";
    }

    private static string ExtractUnsupportedFactualDescriptions(string? reportJson)
    {
        if (string.IsNullOrWhiteSpace(reportJson))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(reportJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var durum = el.TryGetProperty("durum", out var d) ? d.GetString() : null;
                var iddiaTuru = el.TryGetProperty("iddiaTuru", out var it) ? it.GetString() : "olgusal_kaynak_iddiasi";

                if (string.Equals(durum, "desteklenmedi", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(iddiaTuru, "yazar_tezi_yorumu", StringComparison.OrdinalIgnoreCase))
                {
                    var iddia = el.TryGetProperty("iddia", out var i) ? i.GetString() : "";
                    var aciklama = el.TryGetProperty("aciklama", out var a) ? a.GetString() : "";
                    sb.AppendLine($"- {iddia} ({aciklama})");
                }
            }
            return sb.ToString().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
