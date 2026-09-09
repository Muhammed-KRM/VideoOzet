using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.DTOs.Egitim;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EgitimlerController : ControllerBase
{
    private readonly IEgitimService _egitimService;

    public EgitimlerController(IEgitimService egitimService)
    {
        _egitimService = egitimService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _egitimService.GetAllAsync();
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _egitimService.GetByIdAsync(id);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EgitimCreateDto dto)
    {
        var result = await _egitimService.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] EgitimUpdateDto dto)
    {
        if (id != dto.Id)
            return BadRequest("ID in URL does not match ID in body.");

        var result = await _egitimService.UpdateAsync(dto);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _egitimService.DeleteAsync(id);
        return NoContent();
    }
}
