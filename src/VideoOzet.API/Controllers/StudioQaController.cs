using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/qa")]
public class StudioQaController : ControllerBase
{
    private readonly IStudioQaService _qaService;
    private readonly IServiceScopeFactory _scopeFactory;

    public StudioQaController(IStudioQaService qaService, IServiceScopeFactory scopeFactory)
    {
        _qaService = qaService;
        _scopeFactory = scopeFactory;
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

    [HttpGet("status")]
    public IActionResult GetStatus(
        [FromQuery] Guid? egitimId,
        [FromQuery] Guid? contentRequestId,
        [FromQuery] int? bolumNo,
        [FromQuery] Guid? kaynakId,
        [FromQuery] string kapsam = "Bolum")
    {
        bool isProcessing = _qaService.IsThreadProcessing(egitimId, contentRequestId, bolumNo, kaynakId, kapsam);
        return Ok(new { isProcessing });
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] StudioQaAskRequestDto dto, CancellationToken requestAborted = default)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Soru))
            return BadRequest(new { mesaj = "Lütfen bir soru yazın." });

        // Kullanıcı sekme veya sayfa değiştirse bile (requestAborted tetiklense bile)
        // arka planda 10 dakikalık bağımsız scope ile üretimin kesintisiz tamamlanmasını sağla!
        var backgroundTask = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var scopedQa = scope.ServiceProvider.GetRequiredService<IStudioQaService>();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            return await scopedQa.AskAsync(dto, cts.Token);
        });

        try
        {
            var finished = await Task.WhenAny(backgroundTask, Task.Delay(Timeout.Infinite, requestAborted));
            if (finished == backgroundTask)
            {
                var answer = await backgroundTask;
                return Ok(answer);
            }

            // İstemci sayfadan ayrıldı; backgroundTask arka planda veritabanına yazmaya devam ediyor
            _ = backgroundTask.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
            return Accepted(new { mesaj = "Yanıt arka planda hazırlanmaya devam ediyor." });
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
