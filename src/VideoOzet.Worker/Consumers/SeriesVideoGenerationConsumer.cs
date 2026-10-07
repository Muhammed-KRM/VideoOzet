using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Services;

namespace VideoOzet.Worker.Consumers;

public class SeriesVideoGenerationConsumer : IConsumer<SeriesVideoGenerationCommand>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ISourceTopicMapper _topicMapper;
    private readonly ISourceContextBuilder _sourceContextBuilder;
    private readonly IQualityCheckService _qcService;
    private readonly ILogService _logService;
    private readonly ILogger<SeriesVideoGenerationConsumer> _logger;

    public SeriesVideoGenerationConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        IEmbeddingProvider embeddingProvider,
        ISourceTopicMapper topicMapper,
        ILogService logService,
        ILogger<SeriesVideoGenerationConsumer> logger,
        ISourceContextBuilder? sourceContextBuilder = null,
        IQualityCheckService? qcService = null)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _embeddingProvider = embeddingProvider;
        _topicMapper = topicMapper;
        _sourceContextBuilder = sourceContextBuilder ?? new SourceContextBuilder(dbContext, embeddingProvider, Microsoft.Extensions.Logging.Abstractions.NullLogger<SourceContextBuilder>.Instance);
        _qcService = qcService ?? new QualityCheckService(synthesisProvider, Microsoft.Extensions.Logging.Abstractions.NullLogger<QualityCheckService>.Instance);
        _logService = logService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SeriesVideoGenerationCommand> context)
    {
        var message = context.Message;
        var logId = await _logService.LogPipelineStartAsync(null, message.EgitimId, PipelineAsamasi.Sentez, context.CorrelationId?.ToString());

        try
        {
            var request = await _dbContext.ContentRequests
                .Include(r => r.Egitim)
                .FirstOrDefaultAsync(r => r.Id == message.ContentRequestId, context.CancellationToken);

            if (request == null)
            {
                _logger.LogWarning("ContentRequest {Id} bulunamadı.", message.ContentRequestId);
                return;
            }

            var seriPlani = await _dbContext.SeriPlanlari
                .Include(p => p.SeriBolumler)
                .ThenInclude(b => b.Revizyonlar)
                .FirstOrDefaultAsync(p => p.Id == message.SeriPlaniId, context.CancellationToken);
                
            var bolum = seriPlani?.SeriBolumler.FirstOrDefault(b => b.Id == message.SeriBolumId);

            if (seriPlani == null || bolum == null)
            {
                throw new Exception("Seri planı veya bölüm bulunamadı.");
            }

            bolum.Durum = BolumDurumu.Isleniyor;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = $"BolumUretimi-{bolum.BolumNo}",
                Durum = "Basladi",
                Mesaj = $"{bolum.BolumNo}. bölüm içeriği üretiliyor..."
            }, context.CancellationToken);

            // Bölüm konularını ayrıştır
            var topicsList = new List<string>();
            if (!string.IsNullOrWhiteSpace(bolum.KonularJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(bolum.KonularJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            if (elem.ValueKind == JsonValueKind.String)
                            {
                                var s = elem.GetString();
                                if (!string.IsNullOrWhiteSpace(s)) topicsList.Add(s);
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
                catch
                {
                    topicsList.Add(bolum.KonularJson);
                }
            }

            if (!topicsList.Any() && !string.IsNullOrWhiteSpace(bolum.CalismaBasligi))
            {
                topicsList.Add(bolum.CalismaBasligi);
            }

            // Kaynak bağlamını SourceContextBuilder ile gerçek doküman ve videolardan oluştur
            var searchQueries = new List<string> { bolum.CalismaBasligi, bolum.AnaFikir };
            searchQueries.AddRange(topicsList);

            var sourceContext = await _sourceContextBuilder.BuildContextAsync(
                message.EgitimId,
                searchQueries,
                maxChars: 80000,
                ct: context.CancellationToken);

            var allSourcesData = sourceContext.ContextText;
            if (string.IsNullOrWhiteSpace(allSourcesData))
            {
                allSourcesData = await _topicMapper.BuildTopicDigestAsync(message.EgitimId, context.CancellationToken);
            }

            // Önceki bölümün devir notunu bul
            string devirNotu = "";
            if (bolum.BolumNo > 1)
            {
                var oncekiBolum = seriPlani.SeriBolumler.FirstOrDefault(b => b.BolumNo == bolum.BolumNo - 1);
                if (oncekiBolum != null)
                {
                    var sonRevizyon = oncekiBolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault();
                    devirNotu = sonRevizyon?.DevirNotuJson ?? "";
                }
            }

            string userInstructions = seriPlani?.KullaniciKisitlariJson ?? "";

            var contentJsonStr = await _synthesisProvider.GenerateSeriesVideoContentAsync(
                bolum.CalismaBasligi,
                bolum.KonularJson,
                devirNotu,
                request.HedefKitle ?? "Genel",
                allSourcesData,
                userInstructions,
                context.CancellationToken);

            var contentData = LlmJson.Deserialize<JsonElement>(contentJsonStr);
            if (contentData.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Yapay zeka geçerli bir JSON objesi döndürmedi.");
            }

            var usedChunkIds = new List<Guid>(sourceContext.UsedChunkIds);
            if (!usedChunkIds.Any())
            {
                try
                {
                    var queryEmbedding = await _embeddingProvider.GenerateEmbeddingAsync($"{bolum.CalismaBasligi} {bolum.AnaFikir}");
                    var queryVector = new Pgvector.Vector(queryEmbedding);
                    var fallbackChunks = await GetRelevantChunksAsync(message.EgitimId, queryVector, 15, context.CancellationToken);
                    if (fallbackChunks != null && fallbackChunks.Any())
                    {
                        usedChunkIds.AddRange(fallbackChunks.Select(c => c.Id));
                        if (string.IsNullOrWhiteSpace(allSourcesData))
                        {
                            var sb = new StringBuilder();
                            foreach (var c in fallbackChunks)
                            {
                                sb.AppendLine(c.Text);
                            }
                            allSourcesData = sb.ToString();
                        }
                    }
                }
                catch { }
            }

            var yeniVersiyonNo = (bolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault()?.RevizyonNo ?? 0) + 1;
            var kullanilanKaynaklar = usedChunkIds.Any()
                ? JsonSerializer.Serialize(usedChunkIds)
                : (sourceContext.UsedSourceTitles.Any() ? JsonSerializer.Serialize(sourceContext.UsedSourceTitles) : "[]");

            var arastirmaOzeti = contentData.TryGetProperty("ArastirmaOzeti", out var ao) ? ao.GetString() ?? "" : "";
            var videoPlani = contentData.TryGetProperty("VideoPlani", out var vp) ? vp.GetString() ?? "" : "";

            var revizyon = new BolumRevizyonu
            {
                SeriBolumId = bolum.Id,
                RevizyonNo = yeniVersiyonNo,
                Tip = yeniVersiyonNo == 1 ? RevizyonTipi.IlkUretim : RevizyonTipi.KullaniciRevizyonu,
                Talimat = yeniVersiyonNo == 1 ? "Sistem tarafından ilk üretim" : "",
                ArastirmaOzeti = arastirmaOzeti,
                VideoPlani = videoPlani,
                DevirNotuJson = contentData.TryGetProperty("DevirNotu", out var dn) ? dn.GetString() ?? "" : "",
                KullanilanKaynaklar = kullanilanKaynaklar,
                LlmModel = string.IsNullOrWhiteSpace(_synthesisProvider.ActiveModelName) ? "bilinmiyor" : _synthesisProvider.ActiveModelName,
                Durum = BolumDurumu.Tamamlandi
            };

            // ─── Kalite Kontrol (QC) Değerlendirmesi ───
            _logger.LogInformation("Bölüm {BolumNo} için Kalite Kontrol (QC) çalıştırılıyor...", bolum.BolumNo);
            try
            {
                var qcResult = await _qcService.EvaluateAsync(
                    arastirmaOzeti,
                    allSourcesData,
                    claimCount: 8,
                    previousReportJson: null,
                    ct: context.CancellationToken);

                revizyon.GuvenSkorYuzde = qcResult.GuvenSkorYuzde;
                revizyon.ToplamIddiaSayisi = qcResult.ToplamIddiaSayisi;
                revizyon.DesteklenenSayisi = qcResult.DesteklenenSayisi;
                revizyon.BelirsizSayisi = qcResult.BelirsizSayisi;
                revizyon.DesteklenmeyenSayisi = qcResult.DesteklenmeyenSayisi;
                revizyon.DetayliRapor = qcResult.DetayliRaporJson;
                revizyon.QcDurumu = qcResult.Durum;
            }
            catch (Exception qcEx)
            {
                _logger.LogWarning(qcEx, "Bölüm {BolumNo} QC değerlendirmesinde hata oluştu, işlem devam ettiriliyor.", bolum.BolumNo);
                revizyon.QcDurumu = BolumDurumu.Hata;
            }

            // Açık DbSet.Add: SeriBolumId FK'si sayesinde bolum.Revizyonlar navigation'ı da otomatik güncellenir.
            _dbContext.BolumRevizyonlari.Add(revizyon);
            bolum.AktifRevizyonId = revizyon.Id;
            bolum.Durum = BolumDurumu.Tamamlandi;
            
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = $"BolumUretimi-{bolum.BolumNo}",
                Durum = "Tamamlandi",
                Mesaj = $"{bolum.BolumNo}. bölüm başarıyla üretildi."
            }, context.CancellationToken);

            await context.Publish(new SeriesVideoGeneratedEvent
            {
                ContentRequestId = request.Id,
                EgitimId = request.EgitimId,
                SeriBolumId = bolum.Id,
                BolumRevizyonuId = revizyon.Id
            }, context.CancellationToken);
            
            await _logService.LogPipelineEndAsync(logId, $"{{ \"status\": \"Success\", \"bolumNo\": {bolum.BolumNo} }}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeriesVideoGenerationConsumer hata fırlattı: {Message}", ex.Message);

            // Hata yolu ASLA fırlatmamalı (aksi halde MassTransit bölüm üretimini/LLM çağrısını tekrar çalıştırır).
            await ConsumerFailureGuard.TryPersistFailureStateAsync(_dbContext, _logger, async (db, ct) =>
            {
                var bolum = await db.SeriBolumler.FirstOrDefaultAsync(x => x.Id == message.SeriBolumId, ct);
                if (bolum != null)
                {
                    bolum.Durum = BolumDurumu.Hata;
                }
            });

            await ConsumerFailureGuard.TryRunAsync(_logger, "LogFunctionError",
                () => _logService.LogFunctionErrorAsync(nameof(SeriesVideoGenerationConsumer), ex, message));
            await ConsumerFailureGuard.TryRunAsync(_logger, "LogPipelineError",
                () => _logService.LogPipelineErrorAsync(logId, ex));

            await ConsumerFailureGuard.TryRunAsync(_logger, "Publish ContentErrorEvent",
                () => context.Publish(new ContentErrorEvent
                {
                    ContentRequestId = message.ContentRequestId,
                    EgitimId = message.EgitimId,
                    HataMesaji = $"Bölüm {message.BolumNo} üretim hatası: " + ex.Message
                }, CancellationToken.None));
        }
    }

    protected virtual async Task<List<VideoChunkDocument>> GetRelevantChunksAsync(
        Guid egitimId,
        Pgvector.Vector queryVector,
        int limit,
        CancellationToken cancellationToken)
    {
        return await _dbContext.VideoChunkDocuments
            .Where(c => c.EgitimId == egitimId)
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
