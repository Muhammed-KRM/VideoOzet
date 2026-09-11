using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/egitimler/{egitimId:guid}/[controller]")]
public class DosyalarController : ControllerBase
{
    private readonly IVideoService _videoService;
    private readonly IDokumanService _dokumanService;

    public DosyalarController(IVideoService videoService, IDokumanService dokumanService)
    {
        _videoService = videoService;
        _dokumanService = dokumanService;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(1024L * 1024L * 1024L * 2L)] // 2GB limit
    public async Task<IActionResult> UploadFile(Guid egitimId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Dosya yüklenemedi.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        using var stream = file.OpenReadStream();

        // Basit yönlendirme (Video vs Doküman)
        if (ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mkv")
        {
            var dto = new VideoUploadDto
            {
                EgitimId = egitimId,
                FileName = file.FileName,
                ContentType = file.ContentType,
                FileStream = stream
            };
            var result = await _videoService.UploadVideoAsync(dto);
            return Ok(new { tip = "video", id = result });
        }
        else if (ext == ".pdf" || ext == ".docx" || ext == ".pptx" || ext == ".txt")
        {
            var result = await _dokumanService.UploadDokumanAsync(egitimId, file.FileName, file.ContentType, stream);
            return Ok(new { tip = "dokuman", id = result });
        }
        else
        {
            return BadRequest("Desteklenmeyen dosya formatı.");
        }
    }
}
