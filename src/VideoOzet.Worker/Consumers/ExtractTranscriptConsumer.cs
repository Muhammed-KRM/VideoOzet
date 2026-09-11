using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

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

    public ExtractTranscriptConsumer(
        ILogger<ExtractTranscriptConsumer> logger,
        ICacheService cacheService,
        IFileStorageService fileStorageService,
        IAudioExtractor audioExtractor,
        ISttProvider sttProvider,
        AppDbContext dbContext,
        IPublishEndpoint publishEndpoint)
    {
        _logger = logger;
        _cacheService = cacheService;
        _fileStorageService = fileStorageService;
        _audioExtractor = audioExtractor;
        _sttProvider = sttProvider;
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
    }

    public async Task Consume(ConsumeContext<VideoUploadedEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("Received VideoUploadedEvent for VideoId: {VideoId}", message.VideoId);

        // Idempotency check: Ensure we only process this event once
        var lockKey = $"lock:stt:{message.VideoId}";
        var lockAcquired = await _cacheService.SetIfNotExistsAsync(lockKey, "1", TimeSpan.FromHours(1));

        if (!lockAcquired)
        {
            _logger.LogWarning("VideoId: {VideoId} is already being processed or has been processed.", message.VideoId);
            return;
        }

        var tempVideoPath = string.Empty;
        var tempAudioPath = string.Empty;
        var sttStopwatch = new Stopwatch();

        try
        {
            // Update Video Status
            var video = await _dbContext.Videolar.FindAsync(new object[] { message.VideoId }, context.CancellationToken);
            if (video == null)
            {
                _logger.LogError("Video not found in DB: {VideoId}", message.VideoId);
                return;
            }

            video.IslemDurumu = VideoIslemDurumu.SttBasladi;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = message.VideoId,
                Asama = "Video İşleme (STT)",
                Durum = VideoIslemDurumu.SttBasladi.ToString(),
                Mesaj = "Videodan ses çıkarılıyor ve metne dökülüyor..."
            }, context.CancellationToken);

            // Create temp paths
            var tempDir = Path.Combine(Path.GetTempPath(), "VideoOzet");
            Directory.CreateDirectory(tempDir);
            
            var safeVideoName = $"{message.VideoId}{Path.GetExtension(message.DosyaYolu)}";
            tempVideoPath = Path.Combine(tempDir, safeVideoName);
            tempAudioPath = Path.Combine(tempDir, $"{message.VideoId}.mp3");

            // 1. Download Video from Storage
            _logger.LogInformation("Downloading video from storage: {DosyaYolu}", message.DosyaYolu);
            using (var fileStream = new FileStream(tempVideoPath, FileMode.Create, FileAccess.Write))
            {
                // MinioFileService implementation of DownloadFileAsync returns a Stream, so we copy it.
                var storageStream = await _fileStorageService.DownloadFileAsync(message.DosyaYolu);
                await storageStream.CopyToAsync(fileStream, context.CancellationToken);
                storageStream.Dispose();
            }

            // 2. Extract Audio using FFmpeg
            _logger.LogInformation("Extracting audio via FFmpeg");
            var extracted = await _audioExtractor.ExtractAudioAsync(tempVideoPath, tempAudioPath, context.CancellationToken);
            if (!extracted)
            {
                throw new Exception("FFmpeg audio extraction failed.");
            }

            // 3. Transcribe Audio using STT Provider
            _logger.LogInformation("Sending audio to STT Provider");
            sttStopwatch.Start();
            
            string transcriptText;
            using (var audioStream = new FileStream(tempAudioPath, FileMode.Open, FileAccess.Read))
            {
                transcriptText = await _sttProvider.TranscribeAsync(audioStream, Path.GetFileName(tempAudioPath), context.CancellationToken);
            }
            sttStopwatch.Stop();

            if (string.IsNullOrWhiteSpace(transcriptText))
            {
                throw new Exception("STT Provider returned empty transcript.");
            }

            // 4. Save Transcript to Database
            var transcript = new VideoTranscript
            {
                VideoId = message.VideoId,
                HamMetin = transcriptText,
                KelimeSayisi = transcriptText.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length,
                SttModel = "Whisper-1", // Veya configten gelebilir
                SttSuresiMs = (int)sttStopwatch.ElapsedMilliseconds,
                OlusturmaTarihi = DateTime.UtcNow
            };

            _dbContext.VideoTranscripts.Add(transcript);
            
            video.IslemDurumu = VideoIslemDurumu.SttTamamlandi; // STT bitti
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = message.VideoId,
                Asama = "Video İşleme (STT)",
                Durum = VideoIslemDurumu.SttTamamlandi.ToString(),
                Mesaj = "Videodan metin çıkarma tamamlandı."
            }, context.CancellationToken);

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
            
            // Revert status to Error
            var video = await _dbContext.Videolar.FindAsync(new object[] { message.VideoId }, context.CancellationToken);
            if (video != null)
            {
                video.IslemDurumu = VideoIslemDurumu.Hata;
                await _dbContext.SaveChangesAsync(context.CancellationToken);
            }

            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = message.VideoId,
                Asama = "Video İşleme (STT)",
                Durum = VideoIslemDurumu.Hata.ToString(),
                Mesaj = $"STT işlemi sırasında hata oluştu: {ex.Message}"
            }, context.CancellationToken);

            // Remove lock so it can be retried later if desired
            await _cacheService.RemoveAsync(lockKey);
            
            throw; // Re-throw to let MassTransit handle retries or dead-letter queue
        }
        finally
        {
            // Cleanup temp files
            if (!string.IsNullOrEmpty(tempVideoPath) && File.Exists(tempVideoPath))
                File.Delete(tempVideoPath);
                
            if (!string.IsNullOrEmpty(tempAudioPath) && File.Exists(tempAudioPath))
                File.Delete(tempAudioPath);
        }
    }
}
