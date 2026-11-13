using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Services;

namespace VideoOzet.Worker.Consumers;

public class IndexDokumanConsumer : IConsumer<DocumentTextReadyEvent>
{
    private readonly ILogger<IndexDokumanConsumer> _logger;
    private readonly AppDbContext _context;
    private readonly ITextChunker _textChunker;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IPipelineStateTracker? _stateTracker;

    private const int EmbeddingBatchSize = 4;

    public IndexDokumanConsumer(
        ILogger<IndexDokumanConsumer> logger,
        AppDbContext context,
        ITextChunker textChunker,
        IEmbeddingProvider embeddingProvider,
        IPipelineStateTracker? stateTracker = null)
    {
        _logger = logger;
        _context = context;
        _textChunker = textChunker;
        _embeddingProvider = embeddingProvider;
        _stateTracker = stateTracker;
    }

    public async Task Consume(ConsumeContext<DocumentTextReadyEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("IndexDokumanConsumer started for Dokuman: {DokumanId}", evt.DokumanId);

        var dokuman = await _context.Dokumanlar.FindAsync(new object[] { evt.DokumanId }, context.CancellationToken);
        if (dokuman == null) return;

        if (dokuman.IslemDurumu == VideoIslemDurumu.Tamamlandi)
        {
            var hasChunks = await _context.VideoChunkDocuments.AnyAsync(c => c.DokumanId == evt.DokumanId, context.CancellationToken);
            if (hasChunks)
            {
                _logger.LogInformation("Doküman zaten indekslenmiş: {DokumanId}", evt.DokumanId);
                return;
            }
        }

        await using var heartbeat = _stateTracker?.StartHeartbeat(evt.DokumanId, "Doküman İndeksleme");

        try
        {
            dokuman.IslemDurumu = VideoIslemDurumu.IndekslemeBasladi;
            await _context.SaveChangesAsync(context.CancellationToken);

            var startEvt = new PipelineProgressEvent
            {
                VideoId = evt.DokumanId,
                EgitimId = evt.EgitimId,
                Asama = "Doküman İndeksleme",
                Durum = VideoIslemDurumu.IndekslemeBasladi.ToString(),
                Mesaj = "Doküman vektör veritabanına indeksleniyor...",
                Yuzde = 0
            };
            if (_stateTracker != null) await _stateTracker.RecordProgressAsync(startEvt);
            await context.Publish(startEvt, context.CancellationToken);

            var dokumanMetin = await _context.DokumanMetinleri.FindAsync(new object[] { evt.DokumanMetinId }, context.CancellationToken);
            if (dokumanMetin == null)
            {
                dokumanMetin = await _context.DokumanMetinleri.FirstOrDefaultAsync(m => m.DokumanId == evt.DokumanId, context.CancellationToken);
            }
            if (dokumanMetin == null)
            {
                throw new Exception("DokumanMetin bulunamadı.");
            }

            var text = !string.IsNullOrWhiteSpace(dokumanMetin.CevrilmisOzetMetni)
                ? dokumanMetin.CevrilmisOzetMetni
                : (!string.IsNullOrWhiteSpace(dokumanMetin.DamitilmisOzetMetni)
                    ? dokumanMetin.DamitilmisOzetMetni
                    : dokumanMetin.HamMetin);

            var wordCount = dokumanMetin.KelimeSayisi > 0
                ? dokumanMetin.KelimeSayisi
                : text.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

            int maxTokensPerChunk = wordCount > 6000 ? 900 : 500;
            var chunks = _textChunker.ChunkText(text, maxTokensPerChunk);
            if (chunks.Count == 0)
            {
                throw new Exception("Dokümandan indekslenecek metin parçası oluşturulamadı.");
            }

            // Yeniden deneme / kurtarma durumlarında mükerrer parça oluşumunu engelle
            var existingChunks = await _context.VideoChunkDocuments
                .Where(d => d.DokumanId == evt.DokumanId)
                .ToListAsync(context.CancellationToken);
            if (existingChunks.Count > 0)
            {
                _context.VideoChunkDocuments.RemoveRange(existingChunks);
                await _context.SaveChangesAsync(context.CancellationToken);
            }

            var chunkDocuments = new List<VideoChunkDocument>(chunks.Count);
            int processed = 0;

            for (int offset = 0; offset < chunks.Count; offset += EmbeddingBatchSize)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var batch = chunks.Skip(offset).Take(EmbeddingBatchSize).ToList();
                var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(batch);

                for (int i = 0; i < batch.Count; i++)
                {
                    if (i < embeddings.Count && embeddings[i].Length > 0)
                    {
                        chunkDocuments.Add(new VideoChunkDocument
                        {
                            EgitimId = evt.EgitimId,
                            DokumanId = evt.DokumanId,
                            Text = batch[i],
                            Embedding = new Vector(embeddings[i]),
                            StartTimeMs = 0,
                            EndTimeMs = 0,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                processed += batch.Count;
                int percent = Math.Clamp((int)Math.Round((double)processed / chunks.Count * 100), 1, 99);
                var progEvt = new PipelineProgressEvent
                {
                    VideoId = evt.DokumanId,
                    EgitimId = evt.EgitimId,
                    Asama = "Doküman İndeksleme",
                    Durum = VideoIslemDurumu.IndekslemeBasladi.ToString(),
                    Mesaj = $"Vektör indeksleniyor: {processed}/{chunks.Count} parça (%{percent})",
                    Yuzde = percent,
                    MevcutAdim = processed,
                    ToplamAdim = chunks.Count
                };
                if (_stateTracker != null) await _stateTracker.RecordProgressAsync(progEvt);
                await context.Publish(progEvt, context.CancellationToken);
            }

            await _context.VideoChunkDocuments.AddRangeAsync(chunkDocuments, context.CancellationToken);

            dokuman.IslemDurumu = VideoIslemDurumu.Tamamlandi;
            dokuman.IslemTamamlanmaTarihi = DateTime.UtcNow;
            await _context.SaveChangesAsync(context.CancellationToken);

            var doneEvt = new PipelineProgressEvent
            {
                VideoId = evt.DokumanId,
                EgitimId = evt.EgitimId,
                Asama = "Doküman İndeksleme",
                Durum = VideoIslemDurumu.Tamamlandi.ToString(),
                Mesaj = "Doküman indeksleme tamamlandı. Hazır.",
                Yuzde = 100,
                MevcutAdim = chunks.Count,
                ToplamAdim = chunks.Count
            };
            if (_stateTracker != null) await _stateTracker.RecordProgressAsync(doneEvt);
            await context.Publish(doneEvt, context.CancellationToken);

            _logger.LogInformation("Successfully indexed {Count} chunks for DokumanId: {DokumanId}", chunkDocuments.Count, evt.DokumanId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing dokuman {DokumanId}", evt.DokumanId);

            await ConsumerFailureGuard.TryPersistFailureStateAsync(_context, _logger, async (db, ct) =>
            {
                var docEntity = await db.Dokumanlar.FindAsync(new object[] { evt.DokumanId }, ct);
                if (docEntity != null)
                {
                    docEntity.IslemDurumu = VideoIslemDurumu.Hata;
                }
            });

            var errMsg = $"İndeksleme sırasında hata oluştu: {ex.Message}";
            if (_stateTracker != null)
            {
                await ConsumerFailureGuard.TryRunAsync(_logger, "RecordErrorAsync",
                    () => _stateTracker.RecordErrorAsync(evt.DokumanId, evt.EgitimId, "Doküman İndeksleme", errMsg));
            }

            await ConsumerFailureGuard.TryRunAsync(_logger, "PublishDocIndexError", () =>
                context.Publish(new PipelineProgressEvent
                {
                    VideoId = evt.DokumanId,
                    EgitimId = evt.EgitimId,
                    Asama = "Doküman İndeksleme",
                    Durum = VideoIslemDurumu.Hata.ToString(),
                    Mesaj = errMsg
                }, CancellationToken.None));

            throw;
        }
    }
}

public class IndexDokumanConsumerDefinition : ConsumerDefinition<IndexDokumanConsumer>
{
    public IndexDokumanConsumerDefinition()
    {
        ConcurrentMessageLimit = 1;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<IndexDokumanConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.PrefetchCount = 1;
    }
}
