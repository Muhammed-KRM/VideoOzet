using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly IAiProviderSettingsService _settingsService;

    public SettingsController(IAiProviderSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet("ai-provider")]
    public async Task<ActionResult<AiProviderSettingsDto>> GetAiProviderSettings(CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [HttpPost("ai-provider")]
    public async Task<ActionResult<AiProviderSettingsDto>> UpdateAiProviderSettings(
        [FromBody] AiProviderSettingsDto request,
        CancellationToken ct)
    {
        var updated = await _settingsService.UpdateSettingsAsync(request, ct);
        return Ok(updated);
    }

    [HttpPost("ai-provider/test")]
    public async Task<ActionResult<TestConnectionResultDto>> TestAiProviderConnection(
        [FromBody] AiProviderSettingsDto request,
        CancellationToken ct)
    {
        var result = await _settingsService.TestConnectionAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("ai-provider/models")]
    public async Task<ActionResult<List<ModelOptionDto>>> GetAvailableModels(
        [FromQuery] string provider = "openrouter",
        CancellationToken ct = default)
    {
        var models = await _settingsService.GetAvailableModelsAsync(provider, ct);
        return Ok(models);
    }
}
