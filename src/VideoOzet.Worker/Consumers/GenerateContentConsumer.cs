using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Worker.Consumers;

public class GenerateContentConsumer : IConsumer<ContentRequestedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ISourceContextBuilder? _sourceContextBuilder;
    private readonly ILogService _logService;
    private readonly ILogger<GenerateContentConsumer> _logger;

    public GenerateContentConsumer(
        AppDbContext dbContext,
        IEmbeddingProvider embeddingProvider,
        ISynthesisProvider synthesisProvider,
        ILogService logService,
        ILogger<GenerateContentConsumer> logger,
        ISourceContextBuilder? sourceContextBuilder = null)
    {
        _dbContext = dbContext;
        _embeddingProvider = embeddingProvider;
        _synthesisProvider = synthesisProvider;
        _logService = logService;
        _logger = logger;
        _sourceContextBuilder = sourceContextBuilder;
    }

    public async Task Consume(ConsumeContext<ContentRequestedEvent> context)
    {
        var message = context.Message;
        var logId = await _logService.LogPipelineStartAsync(null, message.EgitimId, PipelineAsamasi.Sentez, context.CorrelationId?.ToString());

        try
        {
            var request = await _dbContext.ContentRequests.FindAsync(message.ContentRequestId);
            if (request == null)
            {
                _logger.LogWarning("ContentRequest {Id} bulunamadı.", message.ContentRequestId);
                await _logService.LogPipelineErrorAsync(logId, new Exception("ContentRequest not found."));
                return;
            }

            request.Durum = ContentRequestDurumu.IcerikUretiliyor;
            await _dbContext.SaveChangesAsync();

            var generationStopwatch = Stopwatch.StartNew();

            await context.Publish(new ContentProgressEvent
            {
                ContentRequestId = request.Id,
                EgitimId = message.EgitimId,
                Asama = "Kaynaklar Araştırılıyor",
                Durum = "İşleniyor",
                Yuzde = 10
            });

            // 1 & 2. HedefUzunluk Ölçeklemeli Bağlam (Context) Hazırlama
            var effectiveTargetLength = message.HedefUzunluk ?? request.HedefUzunluk;
            var (scaledMaxChars, scaledTargetChunks) = ResolveTargetContextBudget(effectiveTargetLength);

            string contextData;
            List<Guid> usedChunkIds = new();

            if (_sourceContextBuilder != null)
            {
                var queries = new List<string> { message.Konu };
                if (!string.IsNullOrWhiteSpace(request.EkTonTalimati))
                {
                    queries.Add(request.EkTonTalimati);
                }

                var sourceContext = await _sourceContextBuilder.BuildContextAsync(
                    message.EgitimId,
                    queries,
                    maxChars: scaledMaxChars,
                    preferredSourceIds: null,
                    targetChunksOverride: scaledTargetChunks,
                    ct: context.CancellationToken);

                if (sourceContext == null)
                {
                    sourceContext = await _sourceContextBuilder.BuildContextAsync(
                        message.EgitimId,
                        queries,
                        maxChars: scaledMaxChars,
                        ct: context.CancellationToken) ?? new SourceContextResult();
                }

                contextData = sourceContext.ContextText;
                usedChunkIds = sourceContext.UsedChunkIds;
                if (sourceContext.UncoveredSubTopics != null && sourceContext.UncoveredSubTopics.Count > 0)
                {
                    contextData += $"\n\n[KAPSAM UYARISI: Şu alt başlıklara dair kaynaklarda doğrudan/güçlü ampirik eşleşme bulunamadı: {string.Join("; ", sourceContext.UncoveredSubTopics)}. Bu başlıkları [YAZAR-TEZİ] kavramsal çerçevesiyle açıklayabilirsin ancak asla uydurma istatistik veya sahte kaynak atfı üretme.]";
                }
            }
            else
            {
                var queryEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(message.Konu);
                var queryVector = new Pgvector.Vector(queryEmbedding);
                var topChunks = await GetRelevantChunksAsync(message.EgitimId, queryVector, context.CancellationToken);

                var contextBuilder = new StringBuilder();
                foreach (var chunk in topChunks)
                {
                    contextBuilder.AppendLine($"[Kaynak: VideoId={chunk.VideoId}, Zaman={chunk.StartTimeMs / 1000.0}-{chunk.EndTimeMs / 1000.0}s]");
                    contextBuilder.AppendLine(chunk.Text);
                    contextBuilder.AppendLine("---");
                    usedChunkIds.Add(chunk.Id);
                }
                contextData = contextBuilder.ToString();
            }

            if (!string.IsNullOrWhiteSpace(request.EkTonTalimati))
            {
                contextData = $"[YAZAR-TEZİ VE EK TON DİREKTİFİ]: {request.EkTonTalimati.Trim()}\n\n{contextData}";
            }

            await context.Publish(new ContentProgressEvent
            {
                ContentRequestId = request.Id,
                EgitimId = message.EgitimId,
                Asama = "İçerik Üretiliyor",
                Durum = "İşleniyor",
                Yuzde = 40
            });

            // 4. LLM Üretimi: Araştırma Özeti ve Video Planı
            var summaryTargetLength = message.HedefUzunluk ?? request.HedefUzunluk ?? "Orta Uzunluk";
            var planTargetLength = message.HedefUzunluk ?? request.HedefUzunluk ?? "10 Dakika";

            var arastirmaOzeti = await _synthesisProvider.GenerateResearchSummaryAsync(
                message.Konu,
                summaryTargetLength,
                message.HedefKitle ?? request.HedefKitle ?? "Genel İzleyici",
                contextData,
                context.CancellationToken);

            var videoPlani = await _synthesisProvider.GenerateGroundedVideoPlanAsync(
                message.Konu,
                planTargetLength,
                contextData,
                arastirmaOzeti,
                context.CancellationToken);

            if (string.IsNullOrWhiteSpace(videoPlani))
            {
                videoPlani = await _synthesisProvider.GenerateVideoPlanAsync(
                    message.Konu,
                    planTargetLength,
                    contextData,
                    context.CancellationToken);
            }

            generationStopwatch.Stop();

            // 5. Veritabanına Kaydetme (Gerçek UretimSuresiMs ve LlmModel ile)
            var generatedContent = new GeneratedContent
            {
                ContentRequestId = request.Id,
                ArastirmaOzeti = arastirmaOzeti,
                VideoPlani = videoPlani,
                KullanilanKaynaklar = System.Text.Json.JsonSerializer.Serialize(usedChunkIds),
                LlmModel = string.IsNullOrWhiteSpace(_synthesisProvider.ActiveModelName) ? "bilinmiyor" : _synthesisProvider.ActiveModelName,
                UretimSuresiMs = (int)Math.Max(1, generationStopwatch.ElapsedMilliseconds),
                OlusturmaTarihi = DateTime.UtcNow
            };

            _dbContext.GeneratedContents.Add(generatedContent);

            // QC öncesi durumu güncelle
            request.Durum = ContentRequestDurumu.QcYapiliyor;
            await _dbContext.SaveChangesAsync();

            await context.Publish(new ContentProgressEvent
            {
                ContentRequestId = request.Id,
                EgitimId = message.EgitimId,
                Asama = "Kalite Kontrol (QC) Yapılıyor",
                Durum = "İşleniyor",
                Yuzde = 80
            });

            // 6. Kalite Kontrol (QC) aşamasını tetikle
            await context.Publish(new ContentGeneratedEvent
            {
                ContentRequestId = request.Id,
                EgitimId = message.EgitimId,
                Konu = message.Konu
            });

            await _logService.LogPipelineEndAsync(logId, $"{{ \"status\": \"Success\", \"uretimSuresiMs\": {generatedContent.UretimSuresiMs} }}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "İçerik üretilirken hata oluştu: {Message}", ex.Message);
            try { _dbContext?.ChangeTracker?.Clear(); } catch { }

            try
            {
                await _logService.LogFunctionErrorAsync(nameof(GenerateContentConsumer), ex, message);
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

    private static (int MaxChars, int TargetChunks) ResolveTargetContextBudget(string? targetLength)
    {
        if (string.IsNullOrWhiteSpace(targetLength))
            return (95000, 38);

        var lower = targetLength.Trim().ToLowerInvariant();
        if (lower.Contains("uzun"))
            return (135000, 56);
        if (lower.Contains("kısa") || lower.Contains("kisa"))
            return (65000, 26);

        var minMatch = Regex.Match(lower, @"(\d+)");
        if (minMatch.Success && int.TryParse(minMatch.Groups[1].Value, out int mins))
        {
            if (mins >= 15) return (135000, 56);
            if (mins <= 8) return (65000, 26);
        }

        return (95000, 38);
    }

    protected virtual async Task<List<VideoChunkDocument>> GetRelevantChunksAsync(
        Guid egitimId,
        Pgvector.Vector queryVector,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            var all = await _dbContext.VideoChunkDocuments
                .Where(c => c.EgitimId == egitimId)
                .ToListAsync(cancellationToken);
            return all.Take(15).ToList();
        }

        return await _dbContext.VideoChunkDocuments
            .Where(c => c.EgitimId == egitimId)
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(15)
            .ToListAsync(cancellationToken);
    }
}
