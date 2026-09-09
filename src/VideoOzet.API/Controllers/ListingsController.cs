using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ListingsController : ControllerBase
{
    private readonly IListingService _listingService;

    public ListingsController(IListingService listingService)
    {
        _listingService = listingService;
    }

    [HttpGet("search")]
    [EnableRateLimiting("SearchPolicy")]
    public async Task<ActionResult<SearchResultDto>> Search([FromQuery] SearchFilterDto filters)
        => Ok(await _listingService.SearchAsync(filters));

    [HttpGet("{slug}")]
    public async Task<ActionResult<ListingDto>> GetBySlug(string slug)
    {
        // Guid ise ID ile ara, değilse slug ile
        if (Guid.TryParse(slug, out var id))
        {
            var byId = await _listingService.GetByIdAsync(id);
            return byId is null ? NotFound() : Ok(byId);
        }
        var listing = await _listingService.GetBySlugAsync(slug);
        return listing is null ? NotFound() : Ok(listing);
    }

    [HttpGet("vitrin")]
    public async Task<ActionResult<List<ListingDto>>> GetVitrinListings()
        => Ok(await _listingService.GetVitrinListingsAsync());

    [HttpGet("my")]
    [Authorize]
    public async Task<ActionResult<List<ListingDto>>> GetMyListings()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await _listingService.GetMyListingsAsync(userId));
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ListingDto>> Create(ListingCreateDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _listingService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetBySlug), new { slug = result.Slug }, result);
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<ActionResult<ListingDto>> Update(Guid id, ListingUpdateDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await _listingService.UpdateAsync(id, dto, userId));
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _listingService.DeleteAsync(id, userId);
        return NoContent();
    }
}
