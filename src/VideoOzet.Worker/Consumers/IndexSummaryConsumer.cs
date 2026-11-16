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
    private readonly IContextualTextChunker? _contextualChunker;

    public IndexSummaryConsumer(
        ILogger<IndexSummaryConsumer> logger,
        AppDbContext context,
        ITextChunker chunker,
        IEmbeddingProvider embeddingProvider,
        IPipelineStateTracker? stateTracker = null,
        IContextualTextChunker? contextualChunker = null)
    {
        _logger = logger;
        _context = context;
        _chunker = chunker;
        _embeddingProvider = embeddingProvider;
        _stateTracker = stateTracker;
        _contextualChunker = contextualChunker;
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

            VideoTranscript? transcript = null;
            if (_context.VideoTranscripts != null)
            {
                transcript = await _context.VideoTranscripts
                    .FirstOrDefaultAsync(t => t.VideoId == evt.VideoId, context.CancellationToken);
            }

            Video? videoEntity = null;
            if (_context.Videolar != null)
            {
                videoEntity = await _context.Videolar.FindAsync(new object[] { evt.VideoId }, context.CancellationToken);
            }

            var videoTitle = !string.IsNullOrWhiteSpace(videoEntity?.Baslik) ? videoEntity!.Baslik : $"Video-{evt.VideoId}";
            var safeTitle = videoTitle.Length > 300 ? videoTitle[..300] : videoTitle;
            var videoLang = !string.IsNullOrWhiteSpace(transcript?.Dil) ? transcript!.Dil : "tr";
            var safeLang = videoLang.Length > 16 ? videoLang[..16] : videoLang;
            var usageMode = videoEntity?.KullanimTuru ?? KaynakKullanimTuru.SadeceArgumanVeMantik;
            int? videoDurationSeconds = videoEntity?.Sure != null ? (int?)Math.Round(videoEntity.Sure.Value.TotalSeconds) : null;

            var documents = new List<VideoChunkDocument>();

            bool useFullTranscript = videoEntity?.IndekslemeTuru != IndekslemeTuru.OzetKaynak;
            if (useFullTranscript && _contextualChunker != null && transcript != null && !string.IsNullOrWhiteSpace(transcript.HamMetin))
            {
                var transcriptOptions = new ContextualChunkOptions(
                    KaynakBaslik: videoTitle,
                    KaynakTuru: "VİDEO",
                    Dil: safeLang,
                    KullanimTuru: usageMode,
                    TargetWords: 450,
                    OverlapWords: 70,
                    VideoDurationSeconds: videoDurationSeconds);

                var contextualChunks = _contextualChunker.ChunkTranscript(
                    transcript.HamMetin,
                    transcript.ZamanDamgalari,
                    transcriptOptions).ToList();

                if (!string.IsNullOrWhiteSpace(textToIndex))
                {
                    var cardSlice = textToIndex.Length > 2000 ? textToIndex[..2000] : textToIndex;
                    contextualChunks.Add(new ContextualChunk(
                        ChunkIndex: contextualChunks.Count,
                        ChunkTipi: ChunkTipi.OzetKart,
                        BolumBasligi: "Video Kimlik Kartı",
                        StartTimeMs: 0,
                        EndTimeMs: (videoDurationSeconds ?? 0) * 1000,
                        RawText: cardSlice,
                        ContextualizedText: $"[Kaynak: {safeTitle} | Tür: ÖZET KART | Kimlik Özeti | Dil: {safeLang.ToUpperInvariant()}]\n{cardSlice}"));
                }

                if (contextualChunks.Count == 0)
                {
                    throw new Exception("Video transkriptinden indekslenecek parça üretilemedi.");
                }

                const int batchSize = 4;
                for (int offset = 0; offset < contextualChunks.Count; offset += batchSize)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var batch = contextualChunks.Skip(offset).Take(batchSize).ToList();
                    var batchTexts = batch.Select(c => c.ContextualizedText).ToList();
                    var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(batchTexts);
                    for (int i = 0; i < batch.Count; i++)
                    {
                        if (i < embeddings.Count && embeddings[i].Length > 0)
                        {
                            var chunk = batch[i];
                            documents.Add(new VideoChunkDocument
                            {
                                VideoId = evt.VideoId,
                                EgitimId = evt.EgitimId,
                                ChunkIndex = chunk.ChunkIndex,
                                ChunkTipi = chunk.ChunkTipi,
                                KaynakBaslik = safeTitle,
                                BolumBasligi = chunk.BolumBasligi,
                                KaynakDil = safeLang,
                                KullanimTuru = usageMode,
                                Text = chunk.ContextualizedText,
                                Embedding = new Vector(embeddings[i]),
                                StartTimeMs = chunk.StartTimeMs,
                                EndTimeMs = chunk.EndTimeMs,
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
            }
            else
            {
                var chunks = _chunker.ChunkText(textToIndex);

                if (chunks.Count == 0)
                {
                    throw new Exception("Özet metninden indekslenecek parça üretilemedi.");
                }

                var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(chunks);

                for (int i = 0; i < chunks.Count; i++)
                {
                    if (i < embeddings.Count)
                    {
                        documents.Add(new VideoChunkDocument
                        {
                            VideoId = evt.VideoId,
                            EgitimId = evt.EgitimId,
                            ChunkIndex = i,
                            ChunkTipi = ChunkTipi.OzetKart,
                            KaynakBaslik = safeTitle,
                            KaynakDil = safeLang,
                            KullanimTuru = usageMode,
                            Text = chunks[i],
                            Embedding = new Vector(embeddings[i]),
                            StartTimeMs = 0,
                            EndTimeMs = 0,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
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
