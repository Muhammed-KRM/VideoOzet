using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Worker.Consumers;

public class GenerateContentConsumer : IConsumer<ContentRequestedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ILogService _logService;
    private readonly ILogger<GenerateContentConsumer> _logger;

    public GenerateContentConsumer(
        AppDbContext dbContext,
        IEmbeddingProvider embeddingProvider,
        ISynthesisProvider synthesisProvider,
        ILogService logService,
        ILogger<GenerateContentConsumer> logger)
    {
        _dbContext = dbContext;
        _embeddingProvider = embeddingProvider;
        _synthesisProvider = synthesisProvider;
        _logService = logService;
        _logger = logger;
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

            await context.Publish(new ContentProgressEvent
            {
                ContentRequestId = request.Id,
                EgitimId = message.EgitimId,
                Asama = "Kaynaklar Araştırılıyor",
                Durum = "İşleniyor",
                Yuzde = 10
            });

            // 1. Kullanıcının konusunu Vektöre Çevir
            var queryEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(message.Konu);
            var queryVector = new Pgvector.Vector(queryEmbedding);

            // 2. RAG Arama: pgvector Kosinüs Benzerliği ile en yakın chunk'ları bul (Sadece ilgili Eğitime ait olanlar)
            var topChunks = await GetRelevantChunksAsync(message.EgitimId, queryVector, context.CancellationToken);

            if (!topChunks.Any())
            {
                _logger.LogWarning("EgitimId {EgitimId} için hiç kaynak (chunk) bulunamadı.", message.EgitimId);
            }

            // 3. Bağlam (Context) Hazırlama
            var contextBuilder = new StringBuilder();
            foreach (var chunk in topChunks)
            {
                contextBuilder.AppendLine($"[Kaynak: VideoId={chunk.VideoId}, Zaman={chunk.StartTimeMs / 1000.0}-{chunk.EndTimeMs / 1000.0}s]");
                contextBuilder.AppendLine(chunk.Text);
                contextBuilder.AppendLine("---");
            }
            var contextData = contextBuilder.ToString();

            await context.Publish(new ContentProgressEvent
            {
                ContentRequestId = request.Id,
                EgitimId = message.EgitimId,
                Asama = "İçerik Üretiliyor",
                Durum = "İşleniyor",
                Yuzde = 40
            });

            // 4. LLM Üretimi: Araştırma Özeti ve Video Planı
            var arastirmaOzeti = await _synthesisProvider.GenerateResearchSummaryAsync(
                message.Konu, 
                message.HedefUzunluk ?? "Orta Uzunluk", 
                message.HedefKitle ?? "Genel İzleyici", 
                contextData, 
                context.CancellationToken);

            var videoPlani = await _synthesisProvider.GenerateVideoPlanAsync(
                message.Konu, 
                message.HedefUzunluk ?? "10 Dakika", 
                contextData, 
                context.CancellationToken);

            // 5. Veritabanına Kaydetme
            var generatedContent = new GeneratedContent
            {
                ContentRequestId = request.Id,
                ArastirmaOzeti = arastirmaOzeti,
                VideoPlani = videoPlani,
                KullanilanKaynaklar = System.Text.Json.JsonSerializer.Serialize(topChunks.Select(c => c.Id)),
                LlmModel = "Claude-3.5-Sonnet", // Varsayılan/Config'den alınabilir
                UretimSuresiMs = 0, // Ölçülebilir
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

            await _logService.LogPipelineEndAsync(logId, "{ \"status\": \"Success\" }");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "İçerik üretilirken hata oluştu: {Message}", ex.Message);
            
            _dbContext.ChangeTracker.Clear();

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

    protected virtual async Task<List<VideoChunkDocument>> GetRelevantChunksAsync(
        Guid egitimId, 
        Pgvector.Vector queryVector, 
        CancellationToken cancellationToken)
    {
        return await _dbContext.VideoChunkDocuments
            .Where(c => c.EgitimId == egitimId)
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(15)
            .ToListAsync(cancellationToken);
    }
}
