using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Events;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Infrastructure.Media;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Services;

namespace VideoOzet.Worker.Consumers;

public class DownloadRemoteVideoConsumer : IConsumer<RemoteVideoRequestedEvent>
{
    private readonly ILogger<DownloadRemoteVideoConsumer> _logger;
    private readonly ICacheService _cacheService;
    private readonly IRemoteMediaService _remoteMediaService;
    private readonly ITranscriptQualityEvaluator _qualityEvaluator;
    private readonly IFileStorageService _fileStorageService;
    private readonly AppDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IPipelineStateTracker? _stateTracker;

    public DownloadRemoteVideoConsumer(
        ILogger<DownloadRemoteVideoConsumer> logger,
        ICacheService cacheService,
        IRemoteMediaService remoteMediaService,
        ITranscriptQualityEvaluator qualityEvaluator,
        IFileStorageService fileStorageService,
        AppDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        IPipelineStateTracker? stateTracker = null)
    {
        _logger = logger;
        _cacheService = cacheService;
        _remoteMediaService = remoteMediaService;
        _qualityEvaluator = qualityEvaluator;
        _fileStorageService = fileStorageService;
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _stateTracker = stateTracker;
    }

    public async Task Consume(ConsumeContext<RemoteVideoRequestedEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("Received RemoteVideoRequestedEvent for VideoId: {VideoId}", message.VideoId);

        var lockKey = $"lock:download:{message.VideoId}";
        var lockAcquired = await _cacheService.SetIfNotExistsAsync(lockKey, "1", TimeSpan.FromSeconds(120));
        if (!lockAcquired)
        {
            _logger.LogWarning("VideoId: {VideoId} is already actively downloading.", message.VideoId);
            return;
        }

        string? tempDir = null;
        await using var heartbeat = _stateTracker?.StartHeartbeat(message.VideoId, "Uzaktan İndirme", lockKey);

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
                _logger.LogInformation("VideoId {VideoId} is already completed. Skipping remote download.", message.VideoId);
                return;
            }

            videoEntity.IslemDurumu = VideoIslemDurumu.Indiriliyor;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            var initMsg = message.AlimModu == UzaktanAlimModu.AkilliTranskriptOncelikli
                ? "Altyazı kontrol ediliyor..."
                : "Ses akışı indiriliyor...";

            await PublishProgressAsync(videoEntity.EgitimId, message.VideoId, "Uzaktan İndirme", VideoIslemDurumu.Indiriliyor.ToString(), initMsg, 5, context.CancellationToken);

            if (message.AlimModu == UzaktanAlimModu.AkilliTranskriptOncelikli)
            {
                RemoteCaptionResult? caption = null;

                if (!string.IsNullOrWhiteSpace(message.TranscriptUrl))
                {
                    caption = await _remoteMediaService.TryFetchTranscriptFromUrlAsync(
                        message.TranscriptUrl,
                        message.AuthHeader,
                        message.TercihEdilenDil,
                        context.CancellationToken);
                }
                else if (message.KaynakPlatform == KaynakPlatform.YouTube || YtDlpMediaService.IsYouTubeUrl(message.KaynakUrl))
                {
                    caption = await _remoteMediaService.TryFetchCaptionsAsync(
                        message.KaynakUrl,
                        message.TercihEdilenDil,
                        context.CancellationToken);
                }

                if (caption != null && !string.IsNullOrWhiteSpace(caption.HamMetin))
                {
                    if (caption.SureSaniye.HasValue && caption.SureSaniye.Value > 0 && !videoEntity.Sure.HasValue)
                    {
                        videoEntity.Sure = TimeSpan.FromSeconds(caption.SureSaniye.Value);
                    }

                    int? durationSeconds = caption.SureSaniye
                        ?? (videoEntity.Sure.HasValue ? (int)Math.Round(videoEntity.Sure.Value.TotalSeconds) : null);

                    var evalLang = !string.IsNullOrWhiteSpace(caption.Dil) ? caption.Dil : message.TercihEdilenDil;
                    var report = _qualityEvaluator.Evaluate(
                        caption.HamMetin,
                        durationSeconds,
                        evalLang,
                        caption.IsManual);

                    videoEntity.TranskriptKaliteSkoru = report.Skor;
                    videoEntity.TranskriptKaynakNotu = Truncate(report.OzetNeden, 500);

                    if (report.GectiMi)
                    {
                        var sttModel = caption.IsManual ? "youtube-captions-manual" : "youtube-captions-auto";
                        var wordCount = caption.HamMetin.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

                        var existingTranscript = _dbContext.VideoTranscripts != null
                            ? await _dbContext.VideoTranscripts.FirstOrDefaultAsync(t => t.VideoId == message.VideoId, context.CancellationToken)
                            : null;

                        VideoTranscript transcript;
                        if (existingTranscript != null)
                        {
                            existingTranscript.HamMetin = caption.HamMetin;
                            existingTranscript.ZamanDamgalari = caption.ZamanDamgalariJson;
                            existingTranscript.Dil = string.IsNullOrWhiteSpace(caption.Dil) ? "tr" : caption.Dil;
                            existingTranscript.KelimeSayisi = wordCount;
                            existingTranscript.SttModel = sttModel;
                            existingTranscript.SttSuresiMs = 0;
                            existingTranscript.OlusturmaTarihi = DateTime.UtcNow;
                            transcript = existingTranscript;
                        }
                        else
                        {
                            transcript = new VideoTranscript
                            {
                                VideoId = message.VideoId,
                                HamMetin = caption.HamMetin,
                                ZamanDamgalari = caption.ZamanDamgalariJson,
                                Dil = string.IsNullOrWhiteSpace(caption.Dil) ? "tr" : caption.Dil,
                                KelimeSayisi = wordCount,
                                SttModel = sttModel,
                                SttSuresiMs = 0,
                                OlusturmaTarihi = DateTime.UtcNow
                            };
                            _dbContext.VideoTranscripts?.Add(transcript);
                        }

                        videoEntity.IslemDurumu = VideoIslemDurumu.SttTamamlandi;
                        await _dbContext.SaveChangesAsync(context.CancellationToken);

                        var isHamKaynak = videoEntity.IndekslemeTuru == IndekslemeTuru.HamKaynak;
                        await PublishProgressAsync(
                            videoEntity.EgitimId,
                            message.VideoId,
                            isHamKaynak ? "Indeksleme" : "Ozetleme",
                            VideoIslemDurumu.SttTamamlandi.ToString(),
                            isHamKaynak
                                ? $"Hızlı transkript alındı (Skor: {report.Skor}/100), doğrudan indeksleme kuyruğuna aktarılıyor..."
                                : $"Hızlı transkript alındı (Skor: {report.Skor}/100), özetleme kuyruğuna aktarılıyor...",
                            100,
                            context.CancellationToken);

                        await _publishEndpoint.Publish(new TranscriptReadyEvent
                        {
                            VideoId = message.VideoId,
                            EgitimId = videoEntity.EgitimId,
                            TranscriptId = transcript.Id
                        }, context.CancellationToken);

                        _logger.LogInformation("Fast transcript accepted (Score: {Score}) for VideoId: {VideoId}", report.Skor, message.VideoId);
                        return;
                    }

                    var fallbackMsg = $"Altyazı kalitesi yetersiz (Skor: {report.Skor}/100 — {report.OzetNeden}). Ses akışı indiriliyor...";
                    await _dbContext.SaveChangesAsync(context.CancellationToken);
                    await PublishProgressAsync(
                        videoEntity.EgitimId,
                        message.VideoId,
                        "Uzaktan İndirme",
                        VideoIslemDurumu.Indiriliyor.ToString(),
                        fallbackMsg,
                        15,
                        context.CancellationToken);
                }
                else
                {
                    videoEntity.TranskriptKaynakNotu = "Uygun altyazı bulunamadı; ses akışı indiriliyor.";
                    await _dbContext.SaveChangesAsync(context.CancellationToken);
                    await PublishProgressAsync(
                        videoEntity.EgitimId,
                        message.VideoId,
                        "Uzaktan İndirme",
                        VideoIslemDurumu.Indiriliyor.ToString(),
                        "Altyazı bulunamadı, ses akışı indiriliyor...",
                        15,
                        context.CancellationToken);
                }
            }
            else
            {
                videoEntity.TranskriptKaynakNotu = "Doğrudan ses akışından STT seçildi.";
                await _dbContext.SaveChangesAsync(context.CancellationToken);
            }

            // Fallback veya DogrudanSesStt: ses akışını indirip depolamaya yükle
            tempDir = Path.Combine(Path.GetTempPath(), "VideoOzet", "dl_" + message.VideoId.ToString("N"));
            Directory.CreateDirectory(tempDir);

            long lastProgressTicks = 0;
            var localAudioPath = await _remoteMediaService.DownloadAudioAsync(
                message.KaynakUrl,
                tempDir,
                message.AuthHeader,
                (pct, msg) =>
                {
                    var nowTicks = DateTime.UtcNow.Ticks;
                    if (pct < 100 && nowTicks - Interlocked.Read(ref lastProgressTicks) < TimeSpan.FromSeconds(2).Ticks)
                        return;
                    Interlocked.Exchange(ref lastProgressTicks, nowTicks);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await PublishProgressAsync(
                                videoEntity.EgitimId,
                                message.VideoId,
                                "Uzaktan İndirme",
                                VideoIslemDurumu.Indiriliyor.ToString(),
                                msg,
                                pct,
                                CancellationToken.None);
                        }
                        catch { }
                    });
                },
                context.CancellationToken);

            var fileInfo = new FileInfo(localAudioPath);
            var ext = Path.GetExtension(localAudioPath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".m4a";
            var storageName = $"{Guid.NewGuid()}{ext}";

            string uploadedPath;
            await using (var fs = File.OpenRead(localAudioPath))
            {
                uploadedPath = await _fileStorageService.UploadFileAsync(fs, storageName, GetContentType(ext));
            }

            videoEntity.DosyaYolu = uploadedPath;
            videoEntity.DosyaBoyutu = fileInfo.Length;
            videoEntity.IslemDurumu = VideoIslemDurumu.Bekliyor;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await PublishProgressAsync(
                videoEntity.EgitimId,
                message.VideoId,
                "Bekliyor",
                VideoIslemDurumu.Bekliyor.ToString(),
                "Ses akışı indirildi, STT kuyruğuna alındı...",
                100,
                context.CancellationToken);

            await _publishEndpoint.Publish(new VideoUploadedEvent
            {
                VideoId = videoEntity.Id,
                EgitimId = videoEntity.EgitimId,
                DosyaYolu = uploadedPath,
                UploadedAt = DateTime.UtcNow
            }, context.CancellationToken);

            _logger.LogInformation("Remote audio downloaded & uploaded ({StoragePath}) for VideoId: {VideoId}", uploadedPath, message.VideoId);
        }
        catch (DrmProtectedContentException drmEx)
        {
            _logger.LogWarning(drmEx, "DRM protected content rejected for VideoId: {VideoId}", message.VideoId);
            await PersistFailureAsync(message.VideoId, message.EgitimId, drmEx.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RemoteVideoRequestedEvent for VideoId: {VideoId}", message.VideoId);
            await PersistFailureAsync(message.VideoId, message.EgitimId, $"Uzaktan video indirme hatası: {ex.Message}");
            throw;
        }
        finally
        {
            await _cacheService.RemoveAsync(lockKey);

            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    private async Task PublishProgressAsync(Guid egitimId, Guid videoId, string asama, string durum, string mesaj, int yuzde, CancellationToken ct)
    {
        var evt = new PipelineProgressEvent
        {
            EgitimId = egitimId,
            VideoId = videoId,
            Asama = asama,
            Durum = durum,
            Mesaj = mesaj,
            Yuzde = yuzde,
            MevcutAdim = yuzde >= 100 ? 1 : 0,
            ToplamAdim = 1
        };

        if (_stateTracker != null)
        {
            await _stateTracker.RecordProgressAsync(evt);
        }
        await _publishEndpoint.Publish(evt, ct);
    }

    private async Task PersistFailureAsync(Guid videoId, Guid egitimId, string errorMessage)
    {
        await ConsumerFailureGuard.TryPersistFailureStateAsync(_dbContext, _logger, async (db, ct) =>
        {
            var video = await db.Videolar.FindAsync(new object[] { videoId }, ct);
            if (video != null)
            {
                video.IslemDurumu = VideoIslemDurumu.Hata;
                video.TranskriptKaynakNotu = Truncate(errorMessage, 500);
            }
        });

        if (_stateTracker != null)
        {
            await ConsumerFailureGuard.TryRunAsync(_logger, "RecordErrorAsync",
                () => _stateTracker.RecordErrorAsync(videoId, egitimId, "Uzaktan İndirme", errorMessage));
        }

        await ConsumerFailureGuard.TryRunAsync(_logger, "PublishRemoteDownloadError", () =>
            _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = videoId,
                EgitimId = egitimId,
                Asama = "Uzaktan İndirme",
                Durum = VideoIslemDurumu.Hata.ToString(),
                Mesaj = errorMessage
            }, CancellationToken.None));
    }

    private static string GetContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".wav" => "audio/wav",
        ".webm" => "audio/webm",
        ".ogg" => "audio/ogg",
        _ => "video/mp4"
    };

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

public class DownloadRemoteVideoConsumerDefinition : ConsumerDefinition<DownloadRemoteVideoConsumer>
{
    public DownloadRemoteVideoConsumerDefinition()
    {
        ConcurrentMessageLimit = 2;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<DownloadRemoteVideoConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.PrefetchCount = 2;
    }
}
