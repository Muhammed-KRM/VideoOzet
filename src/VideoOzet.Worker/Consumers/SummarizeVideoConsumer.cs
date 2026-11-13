using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Services;

namespace VideoOzet.Worker.Consumers;

public class SummarizeVideoConsumer : IConsumer<TranscriptReadyEvent>
{
    private readonly ILogger<SummarizeVideoConsumer> _logger;
    private readonly AppDbContext _context;
    private readonly IGeminiProvider _geminiProvider;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IPipelineStateTracker? _stateTracker;
    private static readonly SemaphoreSlim _throttle = new(2, 2);

    public SummarizeVideoConsumer(
        ILogger<SummarizeVideoConsumer> logger,
        AppDbContext context,
        IGeminiProvider geminiProvider,
        IPublishEndpoint publishEndpoint,
        IPipelineStateTracker? stateTracker = null)
    {
        _logger = logger;
        _context = context;
        _geminiProvider = geminiProvider;
        _publishEndpoint = publishEndpoint;
        _stateTracker = stateTracker;
    }

    public async Task Consume(ConsumeContext<TranscriptReadyEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("SummarizeVideoConsumer started for Video: {VideoId}", evt.VideoId);

        await using var heartbeat = _stateTracker?.StartHeartbeat(evt.VideoId, "Ozetleme");

        int maxRetries = 4;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var queuedEvt = new PipelineProgressEvent
                {
                    VideoId = evt.VideoId,
                    EgitimId = evt.EgitimId,
                    Asama = "Ozetleme",
                    Durum = "Bekliyor",
                    Mesaj = attempt > 1
                        ? $"Yapay zeka tekrar deniyor ({attempt}/{maxRetries}). Sıra bekleniyor..."
                        : "Yapay zeka kuyruğuna girdi, sıra bekleniyor..."
                };
                if (_stateTracker != null) await _stateTracker.RecordProgressAsync(queuedEvt);
                await _publishEndpoint.Publish(queuedEvt, context.CancellationToken);

                var transcript = await _context.VideoTranscripts.FirstOrDefaultAsync(t => t.Id == evt.TranscriptId, context.CancellationToken);
                if (transcript == null)
                {
                    _logger.LogWarning("Transcript not found: {TranscriptId}", evt.TranscriptId);
                    return;
                }

                var videoEntity = _context.Videolar != null
                    ? await _context.Videolar.FindAsync(new object[] { evt.VideoId }, context.CancellationToken)
                    : null;
                if (videoEntity != null && videoEntity.IslemDurumu != VideoIslemDurumu.Tamamlandi)
                {
                    videoEntity.IslemDurumu = VideoIslemDurumu.OzetlemeBasladi;
                }

                string jsonResponse;
                await _throttle.WaitAsync(context.CancellationToken);
                try
                {
                    var startEvt = new PipelineProgressEvent
                    {
                        VideoId = evt.VideoId,
                        EgitimId = evt.EgitimId,
                        Asama = "Ozetleme",
                        Durum = "Basladi",
                        Mesaj = "Sıra geldi, yapay zeka transkripti özetliyor...",
                        Yuzde = 70
                    };
                    if (_stateTracker != null) await _stateTracker.RecordProgressAsync(startEvt);
                    await _publishEndpoint.Publish(startEvt, context.CancellationToken);

                    _logger.LogInformation("Calling Gemini API for Video: {VideoId}, transcript length: {Length}", evt.VideoId, transcript.HamMetin?.Length ?? 0);
                    jsonResponse = await _geminiProvider.SummarizeAsync(transcript.HamMetin);
                    _logger.LogInformation("Gemini API responded for Video: {VideoId}, response length: {Length}", evt.VideoId, jsonResponse?.Length ?? 0);
                }
                finally
                {
                    _throttle.Release();
                }

                string ozetMetni = jsonResponse;
                string? cevrilmisOzetMetni = null;
                string detectedLang = transcript.Dil ?? "tr";
                string konuBasliklariJson = "[]";
                string konuEtiketleriJson = "[]";

                try
                {
                    var parsed = VideoOzet.Business.Helpers.LlmJson.Deserialize<SummaryLlmResponse>(jsonResponse);
                    if (parsed != null)
                    {
                        ozetMetni = parsed.OzetMetni ?? jsonResponse;
                        if (!string.IsNullOrWhiteSpace(parsed.Dil))
                        {
                            detectedLang = parsed.Dil.Trim().ToLowerInvariant();
                        }
                        konuBasliklariJson = parsed.KonuBasliklari != null ? System.Text.Json.JsonSerializer.Serialize(parsed.KonuBasliklari) : "[]";
                        konuEtiketleriJson = parsed.KonuEtiketleri != null ? System.Text.Json.JsonSerializer.Serialize(parsed.KonuEtiketleri) : "[]";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LLM json parse failed. Using raw response as summary. Raw: {Raw}", jsonResponse);
                }

                if (detectedLang.Length > 2 && (detectedLang.StartsWith("en") || detectedLang == "english")) detectedLang = "en";
                else if (detectedLang.Length > 2 && (detectedLang.StartsWith("de") || detectedLang == "german")) detectedLang = "de";
                else if (detectedLang.Length > 2 && (detectedLang.StartsWith("tr") || detectedLang == "turkish")) detectedLang = "tr";
                else if (detectedLang.Length > 2) detectedLang = detectedLang.Substring(0, 2);

                transcript.Dil = detectedLang;

                if (!detectedLang.Equals("tr", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Video {VideoId} yabancı dilde tespit edildi ({Lang}). Terim korumalı Türkçe çeviri başlatılıyor...", evt.VideoId, detectedLang);

                    var trEvt = new PipelineProgressEvent
                    {
                        VideoId = evt.VideoId,
                        EgitimId = evt.EgitimId,
                        Asama = "Ceviri",
                        Durum = "Basladi",
                        Mesaj = $"Yabancı içerik ({detectedLang.ToUpperInvariant()}) tespit edildi. Terim korumalı Türkçe çevirisi hazırlanıyor...",
                        Yuzde = 80
                    };
                    if (_stateTracker != null) await _stateTracker.RecordProgressAsync(trEvt);
                    await _publishEndpoint.Publish(trEvt, context.CancellationToken);

                    cevrilmisOzetMetni = await _geminiProvider.TranslateDistilledSummaryToTurkishAsync(ozetMetni, detectedLang);
                    _logger.LogInformation("Video {VideoId} Türkçe çevirisi tamamlandı. Çeviri uzunluğu: {Length}", evt.VideoId, cevrilmisOzetMetni?.Length ?? 0);
                }

                var existingSummary = await _context.VideoSummaries
                    .FirstOrDefaultAsync(s => s.VideoId == evt.VideoId, context.CancellationToken);

                VideoSummary summary;
                if (existingSummary != null)
                {
                    existingSummary.OzetMetni = ozetMetni;
                    existingSummary.CevrilmisOzetMetni = cevrilmisOzetMetni;
                    existingSummary.OrijinalDil = detectedLang;
                    existingSummary.KonuBasliklari = konuBasliklariJson;
                    existingSummary.KonuEtiketleri = konuEtiketleriJson;
                    existingSummary.LlmModel = string.IsNullOrWhiteSpace(_geminiProvider.ActiveModelName) ? "gemini-3.8-flash-tiered" : _geminiProvider.ActiveModelName;
                    existingSummary.OlusturmaTarihi = DateTime.UtcNow;
                    summary = existingSummary;
                }
                else
                {
                    summary = new VideoSummary
                    {
                        VideoId = evt.VideoId,
                        OzetMetni = ozetMetni,
                        CevrilmisOzetMetni = cevrilmisOzetMetni,
                        OrijinalDil = detectedLang,
                        KonuBasliklari = konuBasliklariJson,
                        KonuEtiketleri = konuEtiketleriJson,
                        LlmModel = string.IsNullOrWhiteSpace(_geminiProvider.ActiveModelName) ? "gemini-3.8-flash-tiered" : _geminiProvider.ActiveModelName,
                        OlusturmaTarihi = DateTime.UtcNow
                    };
                    _context.VideoSummaries.Add(summary);
                }

                if (videoEntity != null && videoEntity.IslemDurumu != VideoIslemDurumu.Tamamlandi)
                {
                    videoEntity.IslemDurumu = VideoIslemDurumu.OzetlemeTamamlandi;
                }

                await _context.SaveChangesAsync(context.CancellationToken);

                _logger.LogInformation("Summary completed and saved for Video: {VideoId}", evt.VideoId);

                await context.Publish(new SummaryReadyEvent
                {
                    VideoId = evt.VideoId,
                    EgitimId = evt.EgitimId,
                    SummaryId = summary.Id
                }, context.CancellationToken);

                var doneEvt = new PipelineProgressEvent
                {
                    VideoId = evt.VideoId,
                    EgitimId = evt.EgitimId,
                    Asama = "Ozetleme",
                    Durum = "Tamamlandi",
                    Mesaj = "Özetleme tamamlandı, vektör indekslemeye geçiliyor.",
                    Yuzde = 85
                };
                if (_stateTracker != null) await _stateTracker.RecordProgressAsync(doneEvt);
                await _publishEndpoint.Publish(doneEvt, context.CancellationToken);

                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SummarizeVideo HATA for VideoId: {VideoId}. Attempt: {Attempt}/{MaxRetries}. Exception: {ExType}: {ExMsg}",
                    evt.VideoId, attempt, maxRetries, ex.GetType().Name, ex.Message);

                if (attempt == maxRetries)
                {
                    await ConsumerFailureGuard.TryPersistFailureStateAsync(_context, _logger, async (db, ct) =>
                    {
                        var video = await db.Videolar.FindAsync(new object[] { evt.VideoId }, ct);
                        if (video != null)
                        {
                            video.IslemDurumu = VideoIslemDurumu.Hata;
                        }
                    });

                    var errMessage = $"Özetleme hatası: {ex.Message}";
                    if (_stateTracker != null)
                    {
                        await ConsumerFailureGuard.TryRunAsync(_logger, "RecordErrorAsync",
                            () => _stateTracker.RecordErrorAsync(evt.VideoId, evt.EgitimId, "Ozetleme", errMessage));
                    }

                    await ConsumerFailureGuard.TryRunAsync(_logger, "PublishSummarizeError", () =>
                        _publishEndpoint.Publish(new PipelineProgressEvent
                        {
                            VideoId = evt.VideoId,
                            EgitimId = evt.EgitimId,
                            Asama = "Ozetleme",
                            Durum = "Hata",
                            Mesaj = errMessage
                        }, CancellationToken.None));

                    throw;
                }

                int waitSeconds = 30 * attempt;
                _logger.LogInformation("Waiting {WaitSeconds}s before retry for VideoId: {VideoId}", waitSeconds, evt.VideoId);

                for (int i = waitSeconds; i > 0; i -= 5)
                {
                    var waitEvt = new PipelineProgressEvent
                    {
                        VideoId = evt.VideoId,
                        EgitimId = evt.EgitimId,
                        Asama = "Ozetleme",
                        Durum = "Bekliyor",
                        Mesaj = $"Hata (Limit): {i} saniye sonra tekrar denenecek..."
                    };
                    if (_stateTracker != null) await _stateTracker.RecordProgressAsync(waitEvt);
                    await _publishEndpoint.Publish(waitEvt, context.CancellationToken);

                    int delay = Math.Min(5, i);
                    await Task.Delay(TimeSpan.FromSeconds(delay), context.CancellationToken);
                }
            }
        }
    }

    private class SummaryLlmResponse
    {
        public string? Dil { get; set; }
        public string OzetMetni { get; set; } = string.Empty;
        public string[]? KonuBasliklari { get; set; }
        public string[]? KonuEtiketleri { get; set; }
    }
}

public class SummarizeVideoConsumerDefinition : ConsumerDefinition<SummarizeVideoConsumer>
{
    public SummarizeVideoConsumerDefinition()
    {
        ConcurrentMessageLimit = 2;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<SummarizeVideoConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.PrefetchCount = 2;
    }
}
