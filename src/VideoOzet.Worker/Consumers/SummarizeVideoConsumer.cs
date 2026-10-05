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
    private readonly IPublishEndpoint _publishEndpoint;
    private static readonly System.Threading.SemaphoreSlim _throttle = new System.Threading.SemaphoreSlim(2, 2);

    public SummarizeVideoConsumer(ILogger<SummarizeVideoConsumer> logger, AppDbContext context, IGeminiProvider geminiProvider, IPublishEndpoint publishEndpoint)
    {
        _logger = logger;
        _context = context;
        _geminiProvider = geminiProvider;
        _publishEndpoint = publishEndpoint;
    }

    public async Task Consume(ConsumeContext<TranscriptReadyEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("SummarizeVideoConsumer started for Video: {VideoId}", evt.VideoId);

        int maxRetries = 4;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Kuyruğa girdiğini bildir
                await _publishEndpoint.Publish(new PipelineProgressEvent
                {
                    VideoId = evt.VideoId,
                    EgitimId = evt.EgitimId,
                    Asama = "Ozetleme",
                    Durum = "Bekliyor",
                    Mesaj = attempt > 1 
                        ? $"Yapay zeka tekrar deniyor ({attempt}/{maxRetries}). Sıra bekleniyor..." 
                        : "Yapay zeka kuyruğuna girdi, sıra bekleniyor..."
                });

                var transcript = await _context.VideoTranscripts.FirstOrDefaultAsync(t => t.Id == evt.TranscriptId);
                if (transcript == null)
                {
                    _logger.LogWarning("Transcript not found: {TranscriptId}", evt.TranscriptId);
                    return;
                }

                string jsonResponse;
                await _throttle.WaitAsync(); // Sadece 2 tanesi geçebilir
                try
                {
                    // Sıra geldi, işlemi başlat
                    await _publishEndpoint.Publish(new PipelineProgressEvent
                    {
                        VideoId = evt.VideoId,
                        EgitimId = evt.EgitimId,
                        Asama = "Ozetleme",
                        Durum = "Basladi",
                        Mesaj = "Sıra geldi, yapay zeka transkripti özetliyor..."
                    });

                    _logger.LogInformation("Calling Gemini API for Video: {VideoId}, transcript length: {Length}", evt.VideoId, transcript.HamMetin?.Length ?? 0);
                    jsonResponse = await _geminiProvider.SummarizeAsync(transcript.HamMetin);
                    _logger.LogInformation("Gemini API responded for Video: {VideoId}, response length: {Length}", evt.VideoId, jsonResponse?.Length ?? 0);
                }
                finally
                {
                    _throttle.Release();
                }
                
                string ozetMetni = jsonResponse;
                string konuBasliklariJson = "[]";
                string konuEtiketleriJson = "[]";

                try
                {
                    var parsed = VideoOzet.Business.Helpers.LlmJson.Deserialize<SummaryLlmResponse>(jsonResponse);
                    if (parsed != null)
                    {
                        ozetMetni = parsed.OzetMetni ?? jsonResponse; // Fallback
                        konuBasliklariJson = parsed.KonuBasliklari != null ? System.Text.Json.JsonSerializer.Serialize(parsed.KonuBasliklari) : "[]";
                        konuEtiketleriJson = parsed.KonuEtiketleri != null ? System.Text.Json.JsonSerializer.Serialize(parsed.KonuEtiketleri) : "[]";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LLM json parse failed. Using raw response as summary. Raw: {Raw}", jsonResponse);
                }

                var summary = new VideoSummary
                {
                    VideoId = evt.VideoId,
                    OzetMetni = ozetMetni,
                    KonuBasliklari = konuBasliklariJson, 
                    KonuEtiketleri = konuEtiketleriJson,
                    LlmModel = string.IsNullOrWhiteSpace(_geminiProvider.ActiveModelName) ? "bilinmiyor" : _geminiProvider.ActiveModelName,
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

                await _publishEndpoint.Publish(new PipelineProgressEvent
                {
                    VideoId = evt.VideoId,
                    EgitimId = evt.EgitimId,
                    Asama = "Ozetleme",
                    Durum = "Tamamlandi",
                    Mesaj = "Özetleme tamamlandı, indekslemeye geçiliyor."
                });
                
                return; // Başarılı, çık
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SummarizeVideo HATA for VideoId: {VideoId}. Attempt: {Attempt}/{MaxRetries}. Exception: {ExType}: {ExMsg}", 
                    evt.VideoId, attempt, maxRetries, ex.GetType().Name, ex.Message);
                
                if (attempt == maxRetries)
                {
                    // Son deneme de başarısız, veritabanında Hata olarak işaretle
                    try
                    {
                        var video = await _context.Videolar.FindAsync(new object[] { evt.VideoId });
                        if (video != null)
                        {
                            video.IslemDurumu = VideoOzet.Data.Enums.VideoIslemDurumu.Hata;
                            await _context.SaveChangesAsync();
                        }
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogError(dbEx, "DB update for Hata status also failed for VideoId: {VideoId}", evt.VideoId);
                    }

                    await _publishEndpoint.Publish(new PipelineProgressEvent
                    {
                        VideoId = evt.VideoId,
                        EgitimId = evt.EgitimId,
                        Asama = "Ozetleme",
                        Durum = "Hata",
                        Mesaj = $"Özetleme hatası: {ex.GetType().Name}: {ex.Message}"
                    });
                    
                    throw; // MassTransit error kuyruğuna atsın
                }

                // Hız sınırı veya hata için geri sayımlı bekleme ve arayüze anlık bildirim
                int waitSeconds = 30 * attempt; // Her denemede daha uzun bekle
                _logger.LogInformation("Waiting {WaitSeconds}s before retry for VideoId: {VideoId}", waitSeconds, evt.VideoId);
                
                for (int i = waitSeconds; i > 0; i -= 5)
                {
                    await _publishEndpoint.Publish(new PipelineProgressEvent
                    {
                        VideoId = evt.VideoId,
                        EgitimId = evt.EgitimId,
                        Asama = "Ozetleme",
                        Durum = "Bekliyor",
                        Mesaj = $"Hata (Limit): {i} saniye sonra devam edilecek..."
                    });
                    
                    int delay = Math.Min(5, i);
                    await Task.Delay(TimeSpan.FromSeconds(delay));
                }
            }
        }
    }

    private class SummaryLlmResponse
    {
        public string OzetMetni { get; set; }
        public string[] KonuBasliklari { get; set; }
        public string[] KonuEtiketleri { get; set; }
    }
}
