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
    private readonly IContextualTextChunker? _contextualChunker;

    private const int EmbeddingBatchSize = 4;

    public IndexDokumanConsumer(
        ILogger<IndexDokumanConsumer> logger,
        AppDbContext context,
        ITextChunker textChunker,
        IEmbeddingProvider embeddingProvider,
        IPipelineStateTracker? stateTracker = null,
        IContextualTextChunker? contextualChunker = null)
    {
        _logger = logger;
        _context = context;
        _textChunker = textChunker;
        _embeddingProvider = embeddingProvider;
        _stateTracker = stateTracker;
        _contextualChunker = contextualChunker;
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

            var sourceText = !string.IsNullOrWhiteSpace(dokumanMetin.HamMetin)
                ? dokumanMetin.HamMetin
                : (!string.IsNullOrWhiteSpace(dokumanMetin.CevrilmisOzetMetni)
                    ? dokumanMetin.CevrilmisOzetMetni
                    : dokumanMetin.DamitilmisOzetMetni);

            if (string.IsNullOrWhiteSpace(sourceText))
            {
                throw new Exception("Doküman için indekslenecek metin bulunamadı.");
            }

            var contextualChunks = new List<ContextualChunk>();
            var rawTitle = string.IsNullOrWhiteSpace(dokuman.DosyaAdi) ? $"Doküman-{dokuman.Id}" : dokuman.DosyaAdi;
            var safeTitle = rawTitle.Length > 300 ? rawTitle[..300] : rawTitle;
            var safeLang = string.IsNullOrWhiteSpace(dokumanMetin.OrijinalDil)
                ? "tr"
                : (dokumanMetin.OrijinalDil.Length > 16 ? dokumanMetin.OrijinalDil[..16] : dokumanMetin.OrijinalDil);

            var ozetOnlyText = !string.IsNullOrWhiteSpace(dokumanMetin.CevrilmisOzetMetni)
                ? dokumanMetin.CevrilmisOzetMetni
                : dokumanMetin.DamitilmisOzetMetni;

            if (dokuman.IndekslemeTuru == IndekslemeTuru.OzetKaynak && !string.IsNullOrWhiteSpace(ozetOnlyText))
            {
                var summarySlices = _textChunker.ChunkText(ozetOnlyText, 500);
                for (int si = 0; si < summarySlices.Count; si++)
                {
                    contextualChunks.Add(new ContextualChunk(
                        ChunkIndex: si,
                        ChunkTipi: ChunkTipi.OzetKart,
                        BolumBasligi: summarySlices.Count > 1 ? $"Özet Bölüm {si + 1}" : "Özet Kaynak",
                        StartTimeMs: 0,
                        EndTimeMs: 0,
                        RawText: summarySlices[si],
                        ContextualizedText: $"[Kaynak: {safeTitle} | Tür: ÖZET KAYNAK | Dil: {safeLang.ToUpperInvariant()}]\n{summarySlices[si]}"));
                }
            }
            else if (_contextualChunker != null && !string.IsNullOrWhiteSpace(dokumanMetin.HamMetin))
            {
                var docOptions = new ContextualChunkOptions(
                    KaynakBaslik: rawTitle,
                    KaynakTuru: "DOKÜMAN",
                    Dil: safeLang,
                    KullanimTuru: dokuman.KullanimTuru,
                    TargetWords: 500,
                    OverlapWords: 75);

                contextualChunks.AddRange(_contextualChunker.ChunkDocument(dokumanMetin.HamMetin, docOptions));

                var ozetText = !string.IsNullOrWhiteSpace(dokumanMetin.CevrilmisOzetMetni)
                    ? dokumanMetin.CevrilmisOzetMetni
                    : dokumanMetin.DamitilmisOzetMetni;
                if (!string.IsNullOrWhiteSpace(ozetText))
                {
                    var cardSlice = ozetText.Length > 2000 ? ozetText[..2000] : ozetText;
                    contextualChunks.Add(new ContextualChunk(
                        ChunkIndex: contextualChunks.Count,
                        ChunkTipi: ChunkTipi.OzetKart,
                        BolumBasligi: "Doküman Kimlik Kartı",
                        StartTimeMs: 0,
                        EndTimeMs: 0,
                        RawText: cardSlice,
                        ContextualizedText: $"[Kaynak: {safeTitle} | Tür: ÖZET KART | Kimlik Özeti | Dil: {safeLang.ToUpperInvariant()}]\n{cardSlice}"));
                }
            }
            else
            {
                var plainChunks = _textChunker.ChunkText(sourceText, 500);
                for (int ci = 0; ci < plainChunks.Count; ci++)
                {
                    contextualChunks.Add(new ContextualChunk(
                        ci, ChunkTipi.HamMetin, null, 0, 0, plainChunks[ci], plainChunks[ci]));
                }
            }

            if (contextualChunks.Count == 0)
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

            var chunkDocuments = new List<VideoChunkDocument>(contextualChunks.Count);
            int processed = 0;

            for (int offset = 0; offset < contextualChunks.Count; offset += EmbeddingBatchSize)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var batch = contextualChunks.Skip(offset).Take(EmbeddingBatchSize).ToList();
                var batchTexts = batch.Select(c => c.ContextualizedText).ToList();
                var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(batchTexts);

                for (int i = 0; i < batch.Count; i++)
                {
                    if (i < embeddings.Count && embeddings[i].Length > 0)
                    {
                        var chunk = batch[i];
                        chunkDocuments.Add(new VideoChunkDocument
                        {
                            EgitimId = evt.EgitimId,
                            DokumanId = evt.DokumanId,
                            ChunkIndex = chunk.ChunkIndex,
                            ChunkTipi = chunk.ChunkTipi,
                            KaynakBaslik = safeTitle,
                            BolumBasligi = chunk.BolumBasligi,
                            KaynakDil = safeLang,
                            KullanimTuru = dokuman.KullanimTuru,
                            Text = chunk.ContextualizedText,
                            Embedding = new Vector(embeddings[i]),
                            StartTimeMs = chunk.StartTimeMs,
                            EndTimeMs = chunk.EndTimeMs,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                processed += batch.Count;
                int percent = Math.Clamp((int)Math.Round((double)processed / contextualChunks.Count * 100), 1, 99);
                var progEvt = new PipelineProgressEvent
                {
                    VideoId = evt.DokumanId,
                    EgitimId = evt.EgitimId,
                    Asama = "Doküman İndeksleme",
                    Durum = VideoIslemDurumu.IndekslemeBasladi.ToString(),
                    Mesaj = $"Vektör indeksleniyor: {processed}/{contextualChunks.Count} parça (%{percent})",
                    Yuzde = percent,
                    MevcutAdim = processed,
                    ToplamAdim = contextualChunks.Count
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
                MevcutAdim = contextualChunks.Count,
                ToplamAdim = contextualChunks.Count
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
