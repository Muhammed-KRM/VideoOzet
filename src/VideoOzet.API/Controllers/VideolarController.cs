using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/egitimler/{egitimId:guid}/[controller]")]
public class VideolarController : ControllerBase
{
    private readonly IVideoService _videoService;

    public VideolarController(IVideoService videoService)
    {
        _videoService = videoService;
    }

    [HttpGet]
    public async Task<IActionResult> GetVideosByEgitim(Guid egitimId)
    {
        var result = await _videoService.GetVideosByEgitimIdAsync(egitimId);
        return Ok(result);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(1024L * 1024L * 1024L * 2L)] // 2GB limit
    public async Task<IActionResult> UploadVideo(Guid egitimId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Dosya yüklenemedi.");

        using var stream = file.OpenReadStream();

        var dto = new VideoUploadDto
        {
            EgitimId = egitimId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileStream = stream
        };

        var result = await _videoService.UploadVideoAsync(dto);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteVideo(Guid egitimId, Guid id)
    {
        // egitimId url'den geliyor, ancak silme işlemi doğrudan videoId ile yapılabilir
        await _videoService.DeleteVideoAsync(id);
        return NoContent();
    }
}
