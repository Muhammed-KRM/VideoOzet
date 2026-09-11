using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Worker.Consumers;

public class IndexDokumanConsumer : IConsumer<DocumentTextReadyEvent>
{
    private readonly ILogger<IndexDokumanConsumer> _logger;
    private readonly AppDbContext _context;
    private readonly ITextChunker _textChunker;
    private readonly IEmbeddingProvider _embeddingProvider;

    public IndexDokumanConsumer(
        ILogger<IndexDokumanConsumer> logger,
        AppDbContext context,
        ITextChunker textChunker,
        IEmbeddingProvider embeddingProvider)
    {
        _logger = logger;
        _context = context;
        _textChunker = textChunker;
        _embeddingProvider = embeddingProvider;
    }

    public async Task Consume(ConsumeContext<DocumentTextReadyEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("IndexDokumanConsumer started for Dokuman: {DokumanId}", evt.DokumanId);

        var dokuman = await _context.Dokumanlar.FindAsync(new object[] { evt.DokumanId }, context.CancellationToken);
        if (dokuman == null) return;
        
        dokuman.IslemDurumu = VideoIslemDurumu.IndekslemeBasladi;
        await _context.SaveChangesAsync(context.CancellationToken);

        try
        {
            var dokumanMetin = await _context.DokumanMetinleri.FindAsync(new object[] { evt.DokumanMetinId }, context.CancellationToken);
            if (dokumanMetin == null)
            {
                throw new Exception("DokumanMetin bulunamadı.");
            }

            var text = dokumanMetin.HamMetin;
            var chunks = _textChunker.ChunkText(text, 500);

            var chunkDocuments = new List<VideoChunkDocument>();
            foreach (var chunk in chunks)
            {
                var embedding = await _embeddingProvider.GenerateEmbeddingAsync(chunk);
                
                chunkDocuments.Add(new VideoChunkDocument
                {
                    EgitimId = evt.EgitimId,
                    DokumanId = evt.DokumanId,
                    Text = chunk,
                    Embedding = new Vector(embedding.ToArray()),
                    // StartTime/EndTime ms 0 olarak bırakılabilir çünkü dokümanların zaman damgası yok
                    StartTimeMs = 0,
                    EndTimeMs = 0
                });
            }

            // Bulk Insert
            await _context.VideoChunkDocuments.AddRangeAsync(chunkDocuments, context.CancellationToken);
            
            dokuman.IslemDurumu = VideoIslemDurumu.Tamamlandi;
            await _context.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation("Successfully indexed {Count} chunks for DokumanId: {DokumanId}", chunkDocuments.Count, evt.DokumanId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing dokuman {DokumanId}", evt.DokumanId);
            dokuman.IslemDurumu = VideoIslemDurumu.Hata;
            await _context.SaveChangesAsync(context.CancellationToken);
            throw;
        }
    }
}
