using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewsController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpGet("listing/{listingId}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<ReviewDto>>> GetByListing(Guid listingId)
        => Ok(await _reviewService.GetByListingAsync(listingId));

    [HttpPost]
    public async Task<ActionResult<ReviewDto>> Create([FromBody] System.Text.Json.JsonElement body)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        
        var dto = new ReviewCreateDto
        {
            ListingId = body.GetProperty("listingId").GetGuid(),
            ProfessionalismRating = body.GetProperty("professionalismRating").GetInt32(),
            CommunicationRating = body.GetProperty("communicationRating").GetInt32(),
            ValueRating = body.GetProperty("valueRating").GetInt32(),
            ReviewText = body.TryGetProperty("reviewText", out var rt) ? rt.GetString() ?? "" 
                       : body.TryGetProperty("content", out var c) ? c.GetString() ?? "" : ""
        };
        
        return Ok(await _reviewService.CreateAsync(dto, userId));
    }
}
