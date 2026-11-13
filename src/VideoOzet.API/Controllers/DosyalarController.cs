using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Enums;

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
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> UploadFile(Guid egitimId, IFormFile file, [FromForm] KaynakKullanimTuru kullanimTuru = KaynakKullanimTuru.DogrudanReferans)
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
                FileStream = stream,
                KullanimTuru = kullanimTuru
            };
            var result = await _videoService.UploadVideoAsync(dto);
            return Ok(new { tip = "video", id = result.Id });
        }
        else if (ext == ".pdf" || ext == ".docx" || ext == ".pptx" || ext == ".txt")
        {
            var result = await _dokumanService.UploadDokumanAsync(egitimId, file.FileName, file.ContentType, stream, kullanimTuru);
            return Ok(new { tip = "dokuman", id = result });
        }
        else
        {
            return BadRequest("Desteklenmeyen dosya formatı.");
        }
    }

    [HttpPost("upload-batch")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> UploadBatch(Guid egitimId, [FromForm] KaynakKullanimTuru kullanimTuru = KaynakKullanimTuru.DogrudanReferans)
    {
        var files = Request.Form.Files;
        if (files == null || files.Count == 0)
            return BadRequest("Yüklenecek dosya bulunamadı.");

        var results = new List<object>();

        foreach (var file in files)
        {
            if (file == null || file.Length == 0) continue;

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            using var stream = file.OpenReadStream();

            if (ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mkv")
            {
                var dto = new VideoUploadDto
                {
                    EgitimId = egitimId,
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    FileStream = stream,
                    KullanimTuru = kullanimTuru
                };
                var result = await _videoService.UploadVideoAsync(dto);
                results.Add(new { tip = "video", fileName = file.FileName, id = result.Id });
            }
            else if (ext == ".pdf" || ext == ".docx" || ext == ".pptx" || ext == ".txt")
            {
                var result = await _dokumanService.UploadDokumanAsync(egitimId, file.FileName, file.ContentType, stream, kullanimTuru);
                results.Add(new { tip = "dokuman", fileName = file.FileName, id = result });
            }
        }

        return Ok(results);
    }

    [HttpPatch("{tip}/{id:guid}/kullanim-turu")]
    public async Task<IActionResult> UpdateKullanimTuru(Guid egitimId, string tip, Guid id, [FromBody] UpdateKullanimTuruDto dto)
    {
        if (tip.Equals("video", StringComparison.OrdinalIgnoreCase))
        {
            await _videoService.UpdateKullanimTuruAsync(id, dto.KullanimTuru);
            return Ok(new { success = true, tip = "video", id, kullanimTuru = dto.KullanimTuru });
        }
        else if (tip.Equals("dokuman", StringComparison.OrdinalIgnoreCase))
        {
            await _dokumanService.UpdateKullanimTuruAsync(id, dto.KullanimTuru);
            return Ok(new { success = true, tip = "dokuman", id, kullanimTuru = dto.KullanimTuru });
        }
        else
        {
            return BadRequest("Geçersiz kaynak tipi. 'video' veya 'dokuman' olmalıdır.");
        }
    }

    [HttpGet("dokumanlar/{dokumanId:guid}/icerik")]
    public async Task<IActionResult> GetDokumanIcerik(Guid egitimId, Guid dokumanId)
    {
        var icerik = await _dokumanService.GetDokumanIcerikDetayAsync(dokumanId);
        if (icerik == null)
            return NotFound("Doküman içeriği bulunamadı.");

        return Ok(icerik);
    }

    [HttpDelete("dokumanlar/{id:guid}")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDokuman(Guid egitimId, Guid id)
    {
        await _dokumanService.DeleteDokumanAsync(id);
        return NoContent();
    }

    [HttpPost("dokumanlar/{id:guid}/retry")]
    public async Task<IActionResult> RetryDokuman(Guid egitimId, Guid id)
    {
        await _dokumanService.RetryDokumanAsync(id);
        return Ok(new { message = "Doküman yeniden sıraya alındı." });
    }

    [HttpPost("sorunlulari-kurtar")]
    public async Task<IActionResult> RecoverProblematicFiles(Guid egitimId)
    {
        var videos = await _videoService.GetVideosByEgitimIdAsync(egitimId);
        var docs = await _dokumanService.GetDokumanlarByEgitimIdAsync(egitimId);

        int recoveredVideos = 0;
        foreach (var v in videos)
        {
            if (v.IslemDurumu == VideoIslemDurumu.Hata || v.TakiliMi)
            {
                await _videoService.RetryVideoAsync(v.Id);
                recoveredVideos++;
            }
        }

        int recoveredDocs = 0;
        foreach (var d in docs)
        {
            if (d.IslemDurumu == VideoIslemDurumu.Hata || d.TakiliMi)
            {
                await _dokumanService.RetryDokumanAsync(d.Id);
                recoveredDocs++;
            }
        }

        var total = recoveredVideos + recoveredDocs;
        return Ok(new
        {
            kurtarilanToplam = total,
            kurtarilanVideoSayisi = recoveredVideos,
            kurtarilanDokumanSayisi = recoveredDocs,
            message = total > 0
                ? $"{total} sorunlu dosya ({recoveredVideos} video, {recoveredDocs} doküman) kaldığı aşamadan yeniden sıraya alındı."
                : "Kurtarılacak takılı veya hatalı dosya bulunamadı."
        });
    }

    [HttpGet("dokumanlar")]
    public async Task<IActionResult> GetDokumanlar(Guid egitimId)
    {
        try
        {
            var result = await _dokumanService.GetDokumanlarByEgitimIdAsync(egitimId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message, stack = ex.StackTrace, inner = ex.InnerException?.Message });
        }
    }
}
