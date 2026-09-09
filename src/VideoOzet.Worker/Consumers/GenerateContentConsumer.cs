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
        var logId = await _logService.LogPipelineStartAsync(null, message.EgitimId, PipelineAsamasi.IcerikUretimi, context.CorrelationId?.ToString());

        try
        {
            var request = await _dbContext.ContentRequests.FindAsync(message.ContentRequestId);
            if (request == null)
            {
                _logger.LogWarning("ContentRequest {Id} bulunamadı.", message.ContentRequestId);
                await _logService.LogPipelineErrorAsync(logId, new Exception("ContentRequest not found."));
                return;
            }

            request.Durum = ContentRequestDurumu.IslemeAlindi;
            await _dbContext.SaveChangesAsync();

            // 1. Kullanıcının konusunu Vektöre Çevir
            var queryEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(message.Konu, context.CancellationToken);
            var queryVector = new Pgvector.Vector(queryEmbedding);

            // 2. RAG Arama: pgvector Kosinüs Benzerliği ile en yakın chunk'ları bul (Sadece ilgili Eğitime ait olanlar)
            var topChunks = await _dbContext.VideoChunkDocuments
                .Where(c => c.EgitimId == message.EgitimId)
                .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
                .Take(15) // En ilgili 15 chunk
                .ToListAsync(context.CancellationToken);

            if (!topChunks.Any())
            {
                _logger.LogWarning("EgitimId {EgitimId} için hiç kaynak (chunk) bulunamadı.", message.EgitimId);
            }

            // 3. Bağlam (Context) Hazırlama
            var contextBuilder = new StringBuilder();
            foreach (var chunk in topChunks)
            {
                contextBuilder.AppendLine($"[Kaynak: VideoId={chunk.VideoId}, Zaman={chunk.ZamanBaslangic}-{chunk.ZamanBitis}s]");
                contextBuilder.AppendLine(chunk.Text);
                contextBuilder.AppendLine("---");
            }
            var contextData = contextBuilder.ToString();

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
                KullanilanKaynaklar = string.Join(",", topChunks.Select(c => c.Id)), // Basit loglama, UI'da QC ile detaylanacak
                LlmModel = "Claude-3.5-Sonnet", // Varsayılan/Config'den alınabilir
                UretimSuresiMs = 0, // Ölçülebilir
                OlusturmaTarihi = DateTime.UtcNow
            };

            _dbContext.GeneratedContents.Add(generatedContent);
            
            // QC öncesi durumu güncelle
            request.Durum = ContentRequestDurumu.QcAktarildi;
            await _dbContext.SaveChangesAsync();

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
            await _logService.LogFunctionErrorAsync(nameof(GenerateContentConsumer), ex, message);
            await _logService.LogPipelineErrorAsync(logId, ex);

            var request = await _dbContext.ContentRequests.FindAsync(message.ContentRequestId);
            if (request != null)
            {
                request.Durum = ContentRequestDurumu.Hatali;
                await _dbContext.SaveChangesAsync();
            }
        }
    }
}
