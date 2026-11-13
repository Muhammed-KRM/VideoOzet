using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Enums;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/egitimler/{egitimId:guid}/[controller]")]
public class VideolarController : ControllerBase
{
    private readonly IVideoService _videoService;
    private readonly IDokumanService? _dokumanService;

    public VideolarController(IVideoService videoService, IDokumanService? dokumanService = null)
    {
        _videoService = videoService;
        _dokumanService = dokumanService;
    }

    [HttpGet]
    public async Task<IActionResult> GetVideosByEgitim(Guid egitimId)
    {
        var result = await _videoService.GetVideosByEgitimIdAsync(egitimId);
        return Ok(result);
    }

    [HttpGet("{id:guid}/icerik")]
    public async Task<IActionResult> GetVideoIcerik(Guid egitimId, Guid id)
    {
        var icerik = await _videoService.GetVideoIcerikDetayAsync(id);
        if (icerik == null)
            return NotFound("Video içeriği bulunamadı.");

        return Ok(icerik);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(1024L * 1024L * 1024L * 2L)] // 2GB limit
    public async Task<IActionResult> UploadVideo(Guid egitimId, IFormFile file, [FromForm] KaynakKullanimTuru kullanimTuru = KaynakKullanimTuru.DogrudanReferans)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Dosya yüklenemedi.");

        using var stream = file.OpenReadStream();

        var dto = new VideoUploadDto
        {
            EgitimId = egitimId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileStream = stream,
            KullanimTuru = kullanimTuru
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

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> RetryVideo(Guid egitimId, Guid id)
    {
        await _videoService.RetryVideoAsync(id);
        return Ok(new { message = "Yeniden başlatıldı" });
    }

    [HttpPost("reset-queue")]
    public async Task<IActionResult> ResetQueue(Guid egitimId)
    {
        // 1. RabbitMQ kuyruklarını temizle (HTTP üzerinden)
        using var client = new HttpClient();
        var authBytes = System.Text.Encoding.ASCII.GetBytes("guest:guest");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        
        var queues = new[] { 
            "SummarizeVideo", "SummarizeVideo_error", 
            "ExtractTranscript", "ExtractTranscript_error", 
            "IndexSummary", "IndexSummary_error",
            "QualityCheck", "QualityCheck_error",
            "GenerateContent", "GenerateContent_error",
            "ExtractDokumanText", "ExtractDokumanText_error",
            "IndexDokuman", "IndexDokuman_error"
        };
        foreach(var q in queues)
        {
            try { await client.DeleteAsync($"http://localhost:15673/api/queues/%2f/{q}/contents"); } catch { }
        }

        // 2. Eğitimdeki tamamlanmamış tüm video ve dokümanları yeniden kuyruğa ekle
        await _videoService.ResetQueueForEgitimAsync(egitimId);
        if (_dokumanService != null)
        {
            await _dokumanService.ResetDokumanQueueForEgitimAsync(egitimId);
        }
        
        return Ok(new { message = "Kuyruk sıfırlandı; tamamlanmamış tüm video ve dokümanlar yeniden sıraya alındı." });
    }
}
