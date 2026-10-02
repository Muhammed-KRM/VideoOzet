using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs.Video;

namespace VideoOzet.Business.Interfaces;

public interface IVideoService
{
    Task<VideoListDto> UploadVideoAsync(VideoUploadDto dto);
    Task<IEnumerable<VideoListDto>> GetVideosByEgitimIdAsync(Guid egitimId);
    Task DeleteVideoAsync(Guid id);
    Task RetryVideoAsync(Guid id);
    Task ResetQueueForEgitimAsync(Guid egitimId);
}
