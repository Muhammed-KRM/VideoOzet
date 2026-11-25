using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.DTOs.YouTube;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/youtube")]
public class YouTubeController : ControllerBase
{
    private readonly IYouTubeSearchService _youtubeSearchService;

    public YouTubeController(IYouTubeSearchService youtubeSearchService)
    {
        _youtubeSearchService = youtubeSearchService;
    }

    [HttpGet("search")]
    public async Task<ActionResult<YouTubeSearchResponseDto>> Search(
        [FromQuery] string q,
        [FromQuery] int maxResults = 15,
        [FromQuery] string duration = "any",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new { message = "Arama sorgusu (q) boş olamaz." });
        }

        var clampedMax = Math.Clamp(maxResults, 1, 30);
        var result = await _youtubeSearchService.SearchAsync(q.Trim(), clampedMax, duration, ct);
        return Ok(result);
    }

    [HttpGet("video/{videoId}")]
    public async Task<ActionResult<YouTubeVideoDetailDto>> GetVideoDetail(
        string videoId,
        CancellationToken ct = default)
    {
        if (!YouTubeSearchService.TryNormalizeVideoId(videoId, out var normalizedId))
        {
            return BadRequest(new { message = "Geçersiz YouTube video ID formatı." });
        }

        var detail = await _youtubeSearchService.GetVideoDetailAsync(normalizedId, ct);
        if (detail is null)
        {
            return NotFound(new { message = "YouTube videosu bulunamadı." });
        }

        return Ok(detail);
    }
}
