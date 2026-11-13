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

public class IndexSummaryConsumer : IConsumer<SummaryReadyEvent>
{
    private readonly ILogger<IndexSummaryConsumer> _logger;
    private readonly AppDbContext _context;
    private readonly ITextChunker _chunker;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IPipelineStateTracker? _stateTracker;

    public IndexSummaryConsumer(
        ILogger<IndexSummaryConsumer> logger,
        AppDbContext context,
        ITextChunker chunker,
        IEmbeddingProvider embeddingProvider,
        IPipelineStateTracker? stateTracker = null)
    {
        _logger = logger;
        _context = context;
        _chunker = chunker;
        _embeddingProvider = embeddingProvider;
        _stateTracker = stateTracker;
    }

    public async Task Consume(ConsumeContext<SummaryReadyEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("IndexSummaryConsumer started for Video: {VideoId}", evt.VideoId);

        await using var heartbeat = _stateTracker?.StartHeartbeat(evt.VideoId, "Indeksleme");

        try
        {
            if (_context.Videolar != null)
            {
                var videoStart = await _context.Videolar.FindAsync(new object[] { evt.VideoId }, context.CancellationToken);
                if (videoStart != null && videoStart.IslemDurumu != VideoIslemDurumu.Tamamlandi)
                {
                    videoStart.IslemDurumu = VideoIslemDurumu.IndekslemeBasladi;
                    await _context.SaveChangesAsync(context.CancellationToken);
                }
            }

            var startEvt = new PipelineProgressEvent
            {
                VideoId = evt.VideoId,
                EgitimId = evt.EgitimId,
                Asama = "Indeksleme",
                Durum = "Basladi",
                Mesaj = "Özet vektörlere dönüştürülüp veritabanına kaydediliyor...",
                Yuzde = 90
            };
            if (_stateTracker != null) await _stateTracker.RecordProgressAsync(startEvt);
            await context.Publish(startEvt, context.CancellationToken);

            var summary = await _context.VideoSummaries.FirstOrDefaultAsync(s => s.Id == evt.SummaryId, context.CancellationToken);
            if (summary == null)
            {
                _logger.LogWarning("Summary not found: {SummaryId}", evt.SummaryId);
                return;
            }

            var textToIndex = !string.IsNullOrWhiteSpace(summary.CevrilmisOzetMetni)
                ? summary.CevrilmisOzetMetni
                : summary.OzetMetni;
            var chunks = _chunker.ChunkText(textToIndex);

            if (chunks.Count == 0)
            {
                throw new Exception("Özet metninden indekslenecek parça üretilemedi.");
            }

            var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(chunks);

            var documents = new List<VideoChunkDocument>();
            for (int i = 0; i < chunks.Count; i++)
            {
                if (i < embeddings.Count)
                {
                    documents.Add(new VideoChunkDocument
                    {
                        VideoId = evt.VideoId,
                        EgitimId = evt.EgitimId,
                        Text = chunks[i],
                        Embedding = new Vector(embeddings[i]),
                        StartTimeMs = 0,
                        EndTimeMs = 0,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            var existingChunks = await _context.VideoChunkDocuments
                .Where(d => d.VideoId == evt.VideoId)
                .ToListAsync(context.CancellationToken);
            if (existingChunks.Count > 0)
            {
                _context.VideoChunkDocuments.RemoveRange(existingChunks);
            }

            _context.VideoChunkDocuments.AddRange(documents);

            if (_context.Videolar != null)
            {
                var video = await _context.Videolar.FindAsync(new object[] { evt.VideoId }, context.CancellationToken);
                if (video != null)
                {
                    video.IslemDurumu = VideoIslemDurumu.Tamamlandi;
                    video.IslemTamamlanmaTarihi = DateTime.UtcNow;
                }
            }

            if (_context.Egitimler != null)
            {
                var egitim = await _context.Egitimler.FindAsync(new object[] { evt.EgitimId }, context.CancellationToken);
                if (egitim != null)
                {
                    var islenenSayi = await _context.Videolar
                        .CountAsync(v => v.EgitimId == evt.EgitimId && (v.IslemDurumu == VideoIslemDurumu.Tamamlandi || v.Id == evt.VideoId), context.CancellationToken);
                    egitim.IslenmiVideoSayisi = islenenSayi;
                    if (egitim.ToplamVideoSayisi > 0 && egitim.IslenmiVideoSayisi >= egitim.ToplamVideoSayisi)
                    {
                        egitim.Durum = EgitimDurumu.Tamamlandi;
                    }
                }
            }

            await _context.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation("Indexed {ChunkCount} chunks for Video: {VideoId}", documents.Count, evt.VideoId);

            var doneEvt = new PipelineProgressEvent
            {
                VideoId = evt.VideoId,
                EgitimId = evt.EgitimId,
                Asama = "Tamamlandi",
                Durum = "Tamamlandi",
                Mesaj = "Video başarıyla işlendi ve indekslendi.",
                Yuzde = 100
            };
            if (_stateTracker != null) await _stateTracker.RecordProgressAsync(doneEvt);
            await context.Publish(doneEvt, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing summary for Video {VideoId}", evt.VideoId);

            await ConsumerFailureGuard.TryPersistFailureStateAsync(_context, _logger, async (db, ct) =>
            {
                var video = await db.Videolar.FindAsync(new object[] { evt.VideoId }, ct);
                if (video != null)
                {
                    video.IslemDurumu = VideoIslemDurumu.Hata;
                }
            });

            var errMsg = $"Video indeksleme hatası: {ex.Message}";
            if (_stateTracker != null)
            {
                await ConsumerFailureGuard.TryRunAsync(_logger, "RecordErrorAsync",
                    () => _stateTracker.RecordErrorAsync(evt.VideoId, evt.EgitimId, "Indeksleme", errMsg));
            }

            await ConsumerFailureGuard.TryRunAsync(_logger, "PublishIndexSummaryError", () =>
                context.Publish(new PipelineProgressEvent
                {
                    VideoId = evt.VideoId,
                    EgitimId = evt.EgitimId,
                    Asama = "Indeksleme",
                    Durum = "Hata",
                    Mesaj = errMsg
                }, CancellationToken.None));

            throw;
        }
    }
}

public class IndexSummaryConsumerDefinition : ConsumerDefinition<IndexSummaryConsumer>
{
    public IndexSummaryConsumerDefinition()
    {
        ConcurrentMessageLimit = 1;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<IndexSummaryConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.PrefetchCount = 1;
    }
}
