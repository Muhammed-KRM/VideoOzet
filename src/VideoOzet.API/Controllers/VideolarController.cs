using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration? _configuration;

    public VideolarController(
        IVideoService videoService,
        IDokumanService? dokumanService = null,
        IConfiguration? configuration = null)
    {
        _videoService = videoService;
        _dokumanService = dokumanService;
        _configuration = configuration;
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

    [HttpPost("inspect-url")]
    public async Task<IActionResult> InspectUrl(Guid egitimId, [FromBody] InspectRemoteUrlRequestDto request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Url))
            return BadRequest("Geçerli bir URL girilmelidir.");

        var result = await _videoService.InspectRemoteUrlAsync(egitimId, request, ct);
        return Ok(result);
    }

    [HttpPost("import-remote")]
    public async Task<IActionResult> ImportRemote(Guid egitimId, [FromBody] ImportRemoteVideosRequestDto request, CancellationToken ct)
    {
        if (request == null || request.Items == null || request.Items.Count == 0)
            return BadRequest("İçe aktarılacak en az bir video öğesi seçilmelidir.");

        var result = await _videoService.ImportRemoteVideosAsync(egitimId, request, ct);
        if (result.EklenenVideolar.Count == 0 && result.AtlananDuplicateIdler.Count > 0)
            return Conflict(result);

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
        // 1. RabbitMQ kuyruklarını temizle (HTTP üzerinden, IConfiguration'dan adres ve kimlik bilgileriyle)
        var rabbitHost = _configuration?["RabbitMQ:Host"] ?? _configuration?["RABBITMQ_HOST"] ?? "localhost";
        var managementPort = _configuration?["RabbitMQ:ManagementPort"] ?? _configuration?["RABBITMQ_MANAGEMENT_PORT"] ?? "15673";
        var rabbitUser = _configuration?["RabbitMQ:Username"] ?? _configuration?["RABBITMQ_USER"] ?? "guest";
        var rabbitPass = _configuration?["RabbitMQ:Password"] ?? _configuration?["RABBITMQ_PASSWORD"] ?? "guest";

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var authBytes = System.Text.Encoding.ASCII.GetBytes($"{rabbitUser}:{rabbitPass}");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        
        var baseQueues = new[]
        {
            "DownloadRemoteVideo",
            "SummarizeVideo",
            "ExtractTranscript",
            "IndexSummary",
            "QualityCheck",
            "GenerateContent",
            "ExtractDokumanText",
            "IndexDokuman",
            "TopicAnalysis",
            "TopicAnalysisCompleted",
            "SeriesPlan",
            "SeriesVideoGeneration",
            "SeriesVideoGenerated",
            "SeriesVideoRevision"
        };

        foreach (var q in baseQueues)
        {
            try { await client.DeleteAsync($"http://{rabbitHost}:{managementPort}/api/queues/%2f/{q}/contents"); } catch { }
            try { await client.DeleteAsync($"http://{rabbitHost}:{managementPort}/api/queues/%2f/{q}_error/contents"); } catch { }
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
