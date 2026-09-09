using MassTransit;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using System;

namespace VideoOzet.Worker.Consumers;

public class IndexSummaryConsumer : IConsumer<SummaryReadyEvent>
{
    private readonly ILogger<IndexSummaryConsumer> _logger;
    private readonly AppDbContext _context;
    private readonly ITextChunker _chunker;
    private readonly IEmbeddingProvider _embeddingProvider;

    public IndexSummaryConsumer(ILogger<IndexSummaryConsumer> logger, AppDbContext context, ITextChunker chunker, IEmbeddingProvider embeddingProvider)
    {
        _logger = logger;
        _context = context;
        _chunker = chunker;
        _embeddingProvider = embeddingProvider;
    }

    public async Task Consume(ConsumeContext<SummaryReadyEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("IndexSummaryConsumer started for Video: {VideoId}", evt.VideoId);

        // Durum bildir
        await context.Publish(new PipelineProgressEvent
        {
            VideoId = evt.VideoId,
            EgitimId = evt.EgitimId,
            Asama = "Indeksleme",
            Durum = "Basladi",
            Mesaj = "Özet vektörlere dönüştürülüp veritabanına kaydediliyor..."
        });

        var summary = await _context.VideoSummaries.FirstOrDefaultAsync(s => s.Id == evt.SummaryId);
        if (summary == null)
        {
            _logger.LogWarning("Summary not found: {SummaryId}", evt.SummaryId);
            return;
        }

        // Metni parçala (chunking)
        var chunks = _chunker.ChunkText(summary.OzetMetni);

        if (chunks.Count == 0)
        {
            _logger.LogWarning("No chunks generated for video summary: {VideoId}", evt.VideoId);
            return;
        }

        // Vektörleri al
        var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(chunks);

        // Pgvector nesnelerini oluştur ve veritabanına ekle
        var documents = new System.Collections.Generic.List<VideoChunkDocument>();
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

        _context.VideoChunkDocuments.AddRange(documents);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Indexed {ChunkCount} chunks for Video: {VideoId}", documents.Count, evt.VideoId);

        // Pipeline tamamlandı
        await context.Publish(new PipelineProgressEvent
        {
            VideoId = evt.VideoId,
            EgitimId = evt.EgitimId,
            Asama = "Tamamlandi",
            Durum = "Tamamlandi",
            Mesaj = "Video başarıyla işlendi ve indekslendi."
        });
    }
}
