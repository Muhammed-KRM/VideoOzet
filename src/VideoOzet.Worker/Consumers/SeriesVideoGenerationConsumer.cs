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
    private readonly ILogService _logService;
    private readonly ILogger<SeriesVideoGenerationConsumer> _logger;

    public SeriesVideoGenerationConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        IEmbeddingProvider embeddingProvider,
        ISourceTopicMapper topicMapper,
        ILogService logService,
        ILogger<SeriesVideoGenerationConsumer> logger)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _embeddingProvider = embeddingProvider;
        _topicMapper = topicMapper;
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

            // Bölüm başına pgvector RAG: Konu başına top-5 chunk, max 20 chunk
            var allRetrievedChunks = new List<VideoChunkDocument>();
            const int chunksPerTopic = 5;
            const int maxTotalChunks = 20;

            var titleQuery = $"{bolum.CalismaBasligi} {bolum.AnaFikir}".Trim();
            if (!string.IsNullOrWhiteSpace(titleQuery))
            {
                var titleEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(titleQuery);
                var chunks = await GetRelevantChunksAsync(message.EgitimId, new Pgvector.Vector(titleEmbedding), chunksPerTopic, context.CancellationToken);
                allRetrievedChunks.AddRange(chunks);
            }

            foreach (var topic in topicsList)
            {
                if (allRetrievedChunks.Select(c => c.Id).Distinct().Count() >= maxTotalChunks) break;

                var topicEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(topic);
                var chunks = await GetRelevantChunksAsync(message.EgitimId, new Pgvector.Vector(topicEmbedding), chunksPerTopic, context.CancellationToken);
                allRetrievedChunks.AddRange(chunks);
            }

            var topChunks = allRetrievedChunks
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .Take(maxTotalChunks)
                .ToList();

            var contextBuilder = new StringBuilder();
            if (topChunks.Any())
            {
                foreach (var chunk in topChunks)
                {
                    contextBuilder.AppendLine($"[Kaynak: VideoId={chunk.VideoId}, Zaman={chunk.StartTimeMs / 1000.0:F1}-{chunk.EndTimeMs / 1000.0:F1}s]");
                    contextBuilder.AppendLine(chunk.Text);
                    contextBuilder.AppendLine("---");
                }
            }
            else
            {
                // Fallback: chunk yoksa önbellekli konu özeti kullanılır (ham doküman metinleri
                // yüz binlerce karakter olabilir ve tek prompt'a sığmaz).
                var digest = await _topicMapper.BuildTopicDigestAsync(message.EgitimId, context.CancellationToken);
                contextBuilder.AppendLine(digest);
            }

            var allSourcesData = contextBuilder.ToString();

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

            var contentJsonStr = await _synthesisProvider.GenerateSeriesVideoContentAsync(
                bolum.CalismaBasligi,
                bolum.KonularJson,
                devirNotu,
                request.HedefKitle ?? "Genel",
                allSourcesData,
                context.CancellationToken);

            var contentData = LlmJson.Deserialize<JsonElement>(contentJsonStr);
            if (contentData.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Yapay zeka geçerli bir JSON objesi döndürmedi.");
            }

            var yeniVersiyonNo = (bolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault()?.RevizyonNo ?? 0) + 1;
            var kullanilanKaynaklar = topChunks.Any()
                ? JsonSerializer.Serialize(topChunks.Select(c => c.Id))
                : "[]";

            var revizyon = new BolumRevizyonu
            {
                SeriBolumId = bolum.Id,
                RevizyonNo = yeniVersiyonNo,
                Tip = yeniVersiyonNo == 1 ? RevizyonTipi.IlkUretim : RevizyonTipi.KullaniciRevizyonu,
                Talimat = yeniVersiyonNo == 1 ? "Sistem tarafından ilk üretim" : "",
                ArastirmaOzeti = contentData.TryGetProperty("ArastirmaOzeti", out var ao) ? ao.GetString() ?? "" : "",
                VideoPlani = contentData.TryGetProperty("VideoPlani", out var vp) ? vp.GetString() ?? "" : "",
                DevirNotuJson = contentData.TryGetProperty("DevirNotu", out var dn) ? dn.GetString() ?? "" : "",
                KullanilanKaynaklar = kullanilanKaynaklar,
                LlmModel = string.IsNullOrWhiteSpace(_synthesisProvider.ActiveModelName) ? "bilinmiyor" : _synthesisProvider.ActiveModelName,
                Durum = BolumDurumu.Tamamlandi
            };

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
