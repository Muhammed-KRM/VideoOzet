using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/qa")]
public class StudioQaController : ControllerBase
{
    private readonly IStudioQaService _qaService;

    public StudioQaController(IStudioQaService qaService)
    {
        _qaService = qaService;
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] Guid? egitimId,
        [FromQuery] Guid? contentRequestId,
        [FromQuery] int? bolumNo,
        [FromQuery] Guid? kaynakId,
        [FromQuery] string kapsam = "Bolum",
        CancellationToken ct = default)
    {
        var list = await _qaService.GetHistoryAsync(egitimId, contentRequestId, bolumNo, kaynakId, kapsam, ct);
        return Ok(list);
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] StudioQaAskRequestDto dto, CancellationToken ct = default)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Soru))
            return BadRequest(new { mesaj = "Lütfen bir soru yazın." });

        try
        {
            var answer = await _qaService.AskAsync(dto, ct);
            return Ok(answer);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mesaj = $"Soru yanıtlanırken hata oluştu: {ex.Message}" });
        }
    }

    [HttpPost("apply-suggestion")]
    public async Task<IActionResult> ApplySuggestion([FromBody] StudioQaApplySuggestionDto dto, CancellationToken ct = default)
    {
        var (basarili, mesaj, hedefBolumNo) = await _qaService.ApplySuggestionAsync(dto, ct);
        if (!basarili)
            return BadRequest(new { mesaj, hedefBolumNo });

        return Ok(new { mesaj, hedefBolumNo });
    }

    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistory(
        [FromQuery] Guid? egitimId,
        [FromQuery] Guid? contentRequestId,
        [FromQuery] int? bolumNo,
        [FromQuery] Guid? kaynakId,
        [FromQuery] string kapsam = "Bolum",
        CancellationToken ct = default)
    {
        await _qaService.ClearHistoryAsync(egitimId, contentRequestId, bolumNo, kaynakId, kapsam, ct);
        return Ok(new { mesaj = "Sohbet geçmişi temizlendi." });
    }
}
