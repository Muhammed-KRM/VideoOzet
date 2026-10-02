using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using MassTransit;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Events;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Business.Services;

public class VideoManager : IVideoService
{
    private readonly IRepository<Video> _videoRepository;
    private readonly IRepository<Egitim> _egitimRepository;
    private readonly IRepository<VideoTranscript> _transcriptRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IMapper _mapper;
    private readonly ILogger<VideoManager> _logger;

    public VideoManager(
        IRepository<Video> videoRepository,
        IRepository<Egitim> egitimRepository,
        IRepository<VideoTranscript> transcriptRepository,
        IFileStorageService fileStorageService,
        IPublishEndpoint publishEndpoint,
        IMapper mapper,
        ILogger<VideoManager> logger)
    {
        _videoRepository = videoRepository;
        _egitimRepository = egitimRepository;
        _transcriptRepository = transcriptRepository;
        _fileStorageService = fileStorageService;
        _publishEndpoint = publishEndpoint;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<VideoListDto> UploadVideoAsync(VideoUploadDto dto)
    {
        var egitim = await _egitimRepository.GetByIdAsync(dto.EgitimId);
        if (egitim == null)
            throw new NotFoundException("Egitim", dto.EgitimId);

        // Upload to MinIO
        var fileExtension = System.IO.Path.GetExtension(dto.FileName);
        var minioFileName = $"{Guid.NewGuid()}{fileExtension}";
        
        var uploadedFilePath = await _fileStorageService.UploadFileAsync(dto.FileStream, minioFileName, dto.ContentType);

        // Save to Database
        var video = new Video
        {
            EgitimId = dto.EgitimId,
            Baslik = dto.FileName,
            DosyaYolu = uploadedFilePath,
            IslemDurumu = VideoIslemDurumu.Bekliyor
        };

        await _videoRepository.AddAsync(video);
        
        egitim.ToplamVideoSayisi += 1;
        _egitimRepository.Update(egitim);
        
        await _videoRepository.SaveChangesAsync();

        _logger.LogInformation("Video {VideoId} uploaded and saved to DB.", video.Id);

        // Publish Event to RabbitMQ
        await _publishEndpoint.Publish(new VideoUploadedEvent
        {
            VideoId = video.Id,
            EgitimId = video.EgitimId,
            DosyaYolu = video.DosyaYolu,
            UploadedAt = DateTime.UtcNow
        });

        _logger.LogInformation("VideoUploadedEvent published for video {VideoId}.", video.Id);

        return _mapper.Map<VideoListDto>(video);
    }

    public async Task<IEnumerable<VideoListDto>> GetVideosByEgitimIdAsync(Guid egitimId)
    {
        var egitim = await _egitimRepository.GetByIdAsync(egitimId);
        if (egitim == null)
            throw new NotFoundException("Egitim", egitimId);

        var videolar = await _videoRepository.FindAsync(v => v.EgitimId == egitimId);
        return _mapper.Map<IEnumerable<VideoListDto>>(videolar);
    }

    public async Task DeleteVideoAsync(Guid id)
    {
        var video = await _videoRepository.GetByIdAsync(id);
        if (video == null)
            throw new NotFoundException("Video", id);

        await _fileStorageService.DeleteFileAsync(video.DosyaYolu);

        var egitim = await _egitimRepository.GetByIdAsync(video.EgitimId);
        if (egitim != null && egitim.ToplamVideoSayisi > 0)
        {
            egitim.ToplamVideoSayisi -= 1;
            if (video.IslemDurumu == VideoIslemDurumu.Tamamlandi && egitim.IslenmiVideoSayisi > 0)
                egitim.IslenmiVideoSayisi -= 1;
                
            _egitimRepository.Update(egitim);
        }

        _videoRepository.Delete(video);
        await _videoRepository.SaveChangesAsync();

        _logger.LogInformation("Video {VideoId} deleted.", id);
    }

    public async Task RetryVideoAsync(Guid id)
    {
        var video = await _videoRepository.GetByIdAsync(id);
        if (video == null)
            throw new NotFoundException("Video", id);

        // Zaten transkript var mı kontrol et
        var transcripts = await _transcriptRepository.FindAsync(t => t.VideoId == id);
        var transcript = System.Linq.Enumerable.FirstOrDefault(transcripts);

        if (transcript != null)
        {
            video.IslemDurumu = VideoIslemDurumu.Bekliyor; // Arayüzde tekrar bekliyor görünsün
            _videoRepository.Update(video);
            await _videoRepository.SaveChangesAsync();

            await _publishEndpoint.Publish(new TranscriptReadyEvent
            {
                VideoId = video.Id,
                EgitimId = video.EgitimId,
                TranscriptId = transcript.Id
            });
            
            _logger.LogInformation("Retry triggered from Summarize stage (Transcript exists) for Video {VideoId}", video.Id);
        }
        else
        {
            video.IslemDurumu = VideoIslemDurumu.Bekliyor;
            _videoRepository.Update(video);
            await _videoRepository.SaveChangesAsync();

            await _publishEndpoint.Publish(new VideoUploadedEvent
            {
                VideoId = video.Id,
                EgitimId = video.EgitimId,
                DosyaYolu = video.DosyaYolu,
                UploadedAt = DateTime.UtcNow
            });
            
            _logger.LogInformation("Retry triggered from beginning for Video {VideoId}", video.Id);
        }
    }

    public async Task ResetQueueForEgitimAsync(Guid egitimId)
    {
        var videos = await _videoRepository.FindAsync(v => v.EgitimId == egitimId && v.IslemDurumu != VideoIslemDurumu.Tamamlandi);
        
        foreach (var video in videos)
        {
            var transcripts = await _transcriptRepository.FindAsync(t => t.VideoId == video.Id);
            var transcript = System.Linq.Enumerable.FirstOrDefault(transcripts);

            video.IslemDurumu = VideoIslemDurumu.Bekliyor;
            _videoRepository.Update(video);

            if (transcript != null)
            {
                await _publishEndpoint.Publish(new TranscriptReadyEvent
                {
                    VideoId = video.Id,
                    EgitimId = video.EgitimId,
                    TranscriptId = transcript.Id
                });
            }
            else
            {
                await _publishEndpoint.Publish(new VideoUploadedEvent
                {
                    VideoId = video.Id,
                    EgitimId = video.EgitimId,
                    DosyaYolu = video.DosyaYolu
                });
            }
        }
        await _videoRepository.SaveChangesAsync();
    }
}
