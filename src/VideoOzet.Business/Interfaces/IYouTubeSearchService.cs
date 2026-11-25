using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs.YouTube;

namespace VideoOzet.Business.Interfaces;

public interface IYouTubeSearchService
{
    Task<YouTubeSearchResponseDto> SearchAsync(
        string query,
        int maxResults = 15,
        string duration = "any",
        CancellationToken ct = default);

    Task<YouTubeVideoDetailDto?> GetVideoDetailAsync(
        string videoId,
        CancellationToken ct = default);

    Task<IReadOnlyList<YouTubeSearchItemDto>> GetRelatedOrChannelVideosAsync(
        string videoId,
        string? channelId = null,
        int maxResults = 8,
        CancellationToken ct = default);
}
