using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Events;
using VideoOzet.Business.Infrastructure.AI;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Services;

namespace VideoOzet.Worker.Consumers;

public class ExtractTranscriptConsumer : IConsumer<VideoUploadedEvent>
{
    private readonly ILogger<ExtractTranscriptConsumer> _logger;
    private readonly ICacheService _cacheService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IAudioExtractor _audioExtractor;
    private readonly ISttProvider _sttProvider;
    private readonly AppDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IPipelineStateTracker? _stateTracker;

    public ExtractTranscriptConsumer(
        ILogger<ExtractTranscriptConsumer> logger,
        ICacheService cacheService,
        IFileStorageService fileStorageService,
        IAudioExtractor audioExtractor,
        ISttProvider sttProvider,
        AppDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        IPipelineStateTracker? stateTracker = null)
    {
        _logger = logger;
        _cacheService = cacheService;
        _fileStorageService = fileStorageService;
        _audioExtractor = audioExtractor;
        _sttProvider = sttProvider;
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _stateTracker = stateTracker;
    }

    public async Task Consume(ConsumeContext<VideoUploadedEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("Received VideoUploadedEvent for VideoId: {VideoId}", message.VideoId);

        // Kısa TTL'li kilit + heartbeat ile otomatik yenileme (consumer çökerse 90 sn sonra kilit otomatik kalkar)
        var lockKey = $"lock:stt:{message.VideoId}";
        var lockAcquired = await _cacheService.SetIfNotExistsAsync(lockKey, "1", TimeSpan.FromSeconds(90));

        if (!lockAcquired)
        {
            _logger.LogWarning("VideoId: {VideoId} is already actively locked in STT.", message.VideoId);
            return;
        }

        var tempVideoPath = string.Empty;
        var tempAudioPath = string.Empty;
        var sttStopwatch = new Stopwatch();
        await using var heartbeat = _stateTracker?.StartHeartbeat(message.VideoId, "Video İşleme (STT)", lockKey);

        try
        {
            var videoEntity = await _dbContext.Videolar.FindAsync(new object[] { message.VideoId }, context.CancellationToken);
            if (videoEntity == null)
            {
                _logger.LogError("Video not found in DB: {VideoId}", message.VideoId);
                return;
            }

            if (videoEntity.IslemDurumu == VideoIslemDurumu.Tamamlandi)
            {
                _logger.LogInformation("VideoId {VideoId} is already completed. Skipping STT.", message.VideoId);
                return;
            }

            // Zaten transkript çıkarılmışsa gereksiz GPU STT tekrarını atla ve özetleme aşamasına aktar
            VideoTranscript? existingReadyTranscript = null;
            if (_dbContext.VideoTranscripts != null)
            {
                existingReadyTranscript = await _dbContext.VideoTranscripts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.VideoId == message.VideoId, context.CancellationToken);
            }

            if (existingReadyTranscript != null && !string.IsNullOrWhiteSpace(existingReadyTranscript.HamMetin))
            {
                videoEntity.IslemDurumu = VideoIslemDurumu.SttTamamlandi;
                await _dbContext.SaveChangesAsync(context.CancellationToken);

                var skipEvt = new PipelineProgressEvent
                {
                    VideoId = message.VideoId,
                    EgitimId = videoEntity.EgitimId,
                    Asama = "Ozetleme",
                    Durum = "Bekliyor",
                    Mesaj = "Transkript zaten mevcut, özetleme kuyruğuna aktarılıyor...",
                    Yuzde = 100
                };
                if (_stateTracker != null) await _stateTracker.RecordProgressAsync(skipEvt);
                await _publishEndpoint.Publish(skipEvt, context.CancellationToken);

                await _publishEndpoint.Publish(new TranscriptReadyEvent
                {
                    VideoId = message.VideoId,
                    EgitimId = message.EgitimId,
                    TranscriptId = existingReadyTranscript.Id
                }, context.CancellationToken);
                return;
            }

            videoEntity.IslemDurumu = VideoIslemDurumu.SttBasladi;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            var startEvt = new PipelineProgressEvent
            {
                VideoId = message.VideoId,
                EgitimId = videoEntity.EgitimId,
                Asama = "Video İşleme (STT)",
                Durum = VideoIslemDurumu.SttBasladi.ToString(),
                Mesaj = "Videodan ses çıkarılıyor ve metne dökülüyor...",
                Yuzde = 5,
                MevcutAdim = 0,
                ToplamAdim = 1
            };
            if (_stateTracker != null) await _stateTracker.RecordProgressAsync(startEvt);
            await _publishEndpoint.Publish(startEvt, context.CancellationToken);

            var tempDir = Path.Combine(Path.GetTempPath(), "VideoOzet");
            Directory.CreateDirectory(tempDir);

            var safeVideoName = $"{message.VideoId}{Path.GetExtension(message.DosyaYolu)}";
            tempVideoPath = Path.Combine(tempDir, safeVideoName);
            tempAudioPath = Path.Combine(tempDir, $"{message.VideoId}.mp3");

            // 1. Download Video from Storage
            _logger.LogInformation("Downloading video from storage: {DosyaYolu}", message.DosyaYolu);
            using (var fileStream = new FileStream(tempVideoPath, FileMode.Create, FileAccess.Write))
            {
                var storageStream = await _fileStorageService.DownloadFileAsync(message.DosyaYolu);
                await storageStream.CopyToAsync(fileStream, context.CancellationToken);
                storageStream.Dispose();
            }

            // 2. Extract Audio using FFmpeg
            _logger.LogInformation("Extracting audio via FFmpeg");
            var extracted = await _audioExtractor.ExtractAudioAsync(tempVideoPath, tempAudioPath, context.CancellationToken);
            if (!extracted)
            {
                throw new Exception("FFmpeg ses ayrıştırma işlemi başarısız oldu.");
            }

            // 3. Transcribe Audio using STT Provider
            _logger.LogInformation("Sending audio to STT Provider");
            sttStopwatch.Start();

            long lastProgressPublishTicks = 0;
            if (_sttProvider is ISupportsProgress progressiveProvider)
            {
                progressiveProvider.OnProgress = (done, total) =>
                {
                    var nowTicks = DateTime.UtcNow.Ticks;
                    if (done < total && nowTicks - Interlocked.Read(ref lastProgressPublishTicks) < TimeSpan.FromSeconds(2).Ticks)
                    {
                        return;
                    }
                    Interlocked.Exchange(ref lastProgressPublishTicks, nowTicks);

                    var percent = total > 0 ? Math.Clamp((int)Math.Round((double)done / total * 100), 0, 99) : 0;
                    if (done >= total && total > 0) percent = 100;
                    string progressMsg = $"Transkript çıkarılıyor: {done}/{total} sn (%{percent})";

                    var progEvt = new PipelineProgressEvent
                    {
                        VideoId = message.VideoId,
                        EgitimId = videoEntity.EgitimId,
                        Asama = "STT",
                        Durum = VideoIslemDurumu.SttBasladi.ToString(),
                        Mesaj = progressMsg,
                        Yuzde = percent,
                        MevcutAdim = done,
                        ToplamAdim = total
                    };

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (_stateTracker != null) await _stateTracker.RecordProgressAsync(progEvt);
                            await _publishEndpoint.Publish(progEvt);
                        }
                        catch { }
                    });
                };
            }

            string transcriptText;
            using (var audioStream = new FileStream(tempAudioPath, FileMode.Open, FileAccess.Read))
            {
                transcriptText = await _sttProvider.TranscribeAsync(audioStream, Path.GetFileName(tempAudioPath), context.CancellationToken);
            }
            sttStopwatch.Stop();

            if (string.IsNullOrWhiteSpace(transcriptText))
            {
                throw new Exception("STT sağlayıcısı boş transkript döndürdü.");
            }

            // 4. Save Transcript to Database (Upsert)
            string detectedLang = "tr";
            if (_sttProvider is CompositeSttProvider composite && !string.IsNullOrWhiteSpace(composite.LastDetectedLanguage))
            {
                var l = composite.LastDetectedLanguage.Trim().ToLowerInvariant();
                if (l == "turkish") detectedLang = "tr";
                else if (l == "english") detectedLang = "en";
                else if (l == "german") detectedLang = "de";
                else if (l == "french") detectedLang = "fr";
                else if (l.Length >= 2) detectedLang = l.Substring(0, 2);
            }

            var usedModel = (_sttProvider as CompositeSttProvider)?.LastUsedModel ?? "faster-whisper-large-v3-turbo";

            var existingTranscript = await _dbContext.VideoTranscripts
                .FirstOrDefaultAsync(t => t.VideoId == message.VideoId, context.CancellationToken);

            VideoTranscript transcript;
            if (existingTranscript != null)
            {
                existingTranscript.HamMetin = transcriptText;
                existingTranscript.KelimeSayisi = transcriptText.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                existingTranscript.Dil = detectedLang;
                existingTranscript.SttModel = usedModel;
                existingTranscript.SttSuresiMs = (int)sttStopwatch.ElapsedMilliseconds;
                existingTranscript.OlusturmaTarihi = DateTime.UtcNow;
                transcript = existingTranscript;
            }
            else
            {
                transcript = new VideoTranscript
                {
                    VideoId = message.VideoId,
                    HamMetin = transcriptText,
                    KelimeSayisi = transcriptText.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length,
                    Dil = detectedLang,
                    SttModel = usedModel,
                    SttSuresiMs = (int)sttStopwatch.ElapsedMilliseconds,
                    OlusturmaTarihi = DateTime.UtcNow
                };
                _dbContext.VideoTranscripts.Add(transcript);
            }

            videoEntity.IslemDurumu = VideoIslemDurumu.SttTamamlandi;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            var doneEvt = new PipelineProgressEvent
            {
                VideoId = message.VideoId,
                EgitimId = videoEntity.EgitimId,
                Asama = "Video İşleme (STT)",
                Durum = VideoIslemDurumu.SttTamamlandi.ToString(),
                Mesaj = "Videodan metin çıkarma tamamlandı, özetleme kuyruğuna alındı.",
                Yuzde = 100,
                MevcutAdim = 1,
                ToplamAdim = 1
            };
            if (_stateTracker != null) await _stateTracker.RecordProgressAsync(doneEvt);
            await _publishEndpoint.Publish(doneEvt, context.CancellationToken);

            _logger.LogInformation("Transcript saved successfully for VideoId: {VideoId}", message.VideoId);

            // 5. Publish TranscriptReadyEvent
            await _publishEndpoint.Publish(new TranscriptReadyEvent
            {
                VideoId = message.VideoId,
                EgitimId = message.EgitimId,
                TranscriptId = transcript.Id
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing VideoUploadedEvent for VideoId: {VideoId}", message.VideoId);

            await ConsumerFailureGuard.TryPersistFailureStateAsync(_dbContext, _logger, async (db, ct) =>
            {
                var video = await db.Videolar.FindAsync(new object[] { message.VideoId }, ct);
                if (video != null)
                {
                    video.IslemDurumu = VideoIslemDurumu.Hata;
                }
            });

            var errMessage = $"STT işlemi sırasında hata oluştu: {ex.Message}";
            if (_stateTracker != null)
            {
                await ConsumerFailureGuard.TryRunAsync(_logger, "RecordErrorAsync",
                    () => _stateTracker.RecordErrorAsync(message.VideoId, message.EgitimId, "Video İşleme (STT)", errMessage));
            }

            await ConsumerFailureGuard.TryRunAsync(_logger, "PublishSttError", () =>
                _publishEndpoint.Publish(new PipelineProgressEvent
                {
                    VideoId = message.VideoId,
                    EgitimId = message.EgitimId,
                    Asama = "Video İşleme (STT)",
                    Durum = VideoIslemDurumu.Hata.ToString(),
                    Mesaj = errMessage
                }, CancellationToken.None));

            throw;
        }
        finally
        {
            await _cacheService.RemoveAsync(lockKey);

            if (!string.IsNullOrEmpty(tempVideoPath) && File.Exists(tempVideoPath))
            {
                try { File.Delete(tempVideoPath); } catch { }
            }

            if (!string.IsNullOrEmpty(tempAudioPath) && File.Exists(tempAudioPath))
            {
                try { File.Delete(tempAudioPath); } catch { }
            }
        }
    }
}

public class ExtractTranscriptConsumerDefinition : ConsumerDefinition<ExtractTranscriptConsumer>
{
    public ExtractTranscriptConsumerDefinition()
    {
        ConcurrentMessageLimit = 1;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ExtractTranscriptConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.PrefetchCount = 1;
    }
}
