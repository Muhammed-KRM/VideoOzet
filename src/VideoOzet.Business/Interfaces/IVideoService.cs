using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Interfaces;

public interface IVideoService
{
    Task<VideoListDto> UploadVideoAsync(VideoUploadDto dto);
    Task<IEnumerable<VideoListDto>> GetVideosByEgitimIdAsync(Guid egitimId);
    Task DeleteVideoAsync(Guid id);
    Task RetryVideoAsync(Guid id);
    Task ResetQueueForEgitimAsync(Guid egitimId);
    Task UpdateKullanimTuruAsync(Guid id, KaynakKullanimTuru kullanimTuru);
    Task<KaynakIcerikDetayDto?> GetVideoIcerikDetayAsync(Guid id);
}
