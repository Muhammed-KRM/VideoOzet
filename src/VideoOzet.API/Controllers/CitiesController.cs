using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CitiesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICacheService _cacheService;

    public CitiesController(AppDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        const string cacheKey = "cities:all";
        var cached = await _cacheService.GetAsync<object>(cacheKey);
        if (cached != null) return Ok(cached);

        var cities = await _context.Cities
            .Include(c => c.Districts)
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Slug,
                c.PlateCode,
                Districts = c.Districts.OrderBy(d => d.Name).Select(d => new { d.Id, d.Name, d.Slug })
            })
            .ToListAsync();

        await _cacheService.SetAsync(cacheKey, cities, TimeSpan.FromHours(1));
        return Ok(cities);
    }

    [HttpGet("{cityId}/districts")]
    public async Task<IActionResult> GetDistricts(int cityId)
    {
        var cacheKey = $"cities:{cityId}:districts";
        var cached = await _cacheService.GetAsync<object>(cacheKey);
        if (cached != null) return Ok(cached);

        var districts = await _context.Districts
            .Where(d => d.CityId == cityId)
            .OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name, d.Slug })
            .ToListAsync();

        await _cacheService.SetAsync(cacheKey, districts, TimeSpan.FromHours(1));
        return Ok(districts);
    }
}

