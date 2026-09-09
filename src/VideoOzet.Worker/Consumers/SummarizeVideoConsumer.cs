using MassTransit;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System;

namespace VideoOzet.Worker.Consumers;

public class SummarizeVideoConsumer : IConsumer<TranscriptReadyEvent>
{
    private readonly ILogger<SummarizeVideoConsumer> _logger;
    private readonly AppDbContext _context;
    private readonly IGeminiProvider _geminiProvider;

    public SummarizeVideoConsumer(ILogger<SummarizeVideoConsumer> logger, AppDbContext context, IGeminiProvider geminiProvider)
    {
        _logger = logger;
        _context = context;
        _geminiProvider = geminiProvider;
    }

    public async Task Consume(ConsumeContext<TranscriptReadyEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("SummarizeVideoConsumer started for Video: {VideoId}", evt.VideoId);

        // SignalR için durum bildir
        await context.Publish(new PipelineProgressEvent
        {
            VideoId = evt.VideoId,
            EgitimId = evt.EgitimId,
            Asama = "Ozetleme",
            Durum = "Basladi",
            Mesaj = "Yapay zeka transkripti özetliyor..."
        });

        var transcript = await _context.VideoTranscripts.FirstOrDefaultAsync(t => t.Id == evt.TranscriptId);
        if (transcript == null)
        {
            _logger.LogWarning("Transcript not found: {TranscriptId}", evt.TranscriptId);
            return;
        }

        // Özetleme İşlemi
        var jsonResponse = await _geminiProvider.SummarizeAsync(transcript.HamMetin);
        
        // Basit parsing (geliştirilebilir)
        var summary = new VideoSummary
        {
            VideoId = evt.VideoId,
            OzetMetni = jsonResponse, // İdealde JSON deserialize edilerek alanlara bölünür
            KonuBasliklari = "[]", 
            KonuEtiketleri = "[]",
            LlmModel = "gemini-2.0-flash",
            OlusturmaTarihi = DateTime.UtcNow
        };

        _context.VideoSummaries.Add(summary);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Summary completed and saved for Video: {VideoId}", evt.VideoId);

        await context.Publish(new SummaryReadyEvent
        {
            VideoId = evt.VideoId,
            EgitimId = evt.EgitimId,
            SummaryId = summary.Id
        });

        // Durum bildir
        await context.Publish(new PipelineProgressEvent
        {
            VideoId = evt.VideoId,
            EgitimId = evt.EgitimId,
            Asama = "Ozetleme",
            Durum = "Tamamlandi",
            Mesaj = "Özetleme tamamlandı, indekslemeye geçiliyor."
        });
    }
}
