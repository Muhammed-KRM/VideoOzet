using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Enums;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SitemapController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICacheService _cacheService;

    public SitemapController(AppDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    [HttpGet]
    [Produces("application/xml")]
    public async Task<IActionResult> GetSitemap()
    {
        const string cacheKey = "sitemap:xml";
        var cached = await _cacheService.GetAsync<string>(cacheKey);
        
        if (!string.IsNullOrEmpty(cached))
        {
            return Content(cached, "application/xml", Encoding.UTF8);
        }

        var baseUrl = "https://VideoOzet.com";
        var urls = new List<SitemapUrl>();

        // 1. Statik Kök ve Sayfalar
        urls.Add(new SitemapUrl { Loc = $"{baseUrl}/", ChangeFreq = "daily", Priority = 1.0 });
        urls.Add(new SitemapUrl { Loc = $"{baseUrl}/arama", ChangeFreq = "hourly", Priority = 0.9 });
        urls.Add(new SitemapUrl { Loc = $"{baseUrl}/nasil-calisir", ChangeFreq = "monthly", Priority = 0.7 });
        urls.Add(new SitemapUrl { Loc = $"{baseUrl}/hakkimizda", ChangeFreq = "monthly", Priority = 0.6 });
        urls.Add(new SitemapUrl { Loc = $"{baseUrl}/sss", ChangeFreq = "monthly", Priority = 0.6 });

        // 2. Şehir Dizin Sayfaları
        var cities = await _context.Cities.Select(c => c.Slug).ToListAsync();
        foreach (var city in cities)
        {
            urls.Add(new SitemapUrl { Loc = $"{baseUrl}/{city}/ozel-ders-verenler", ChangeFreq = "daily", Priority = 0.8 });
        }

        // 3. Branş Dizin Sayfaları
        var branches = await _context.Branches.Select(b => b.Slug).ToListAsync();
        foreach (var branch in branches)
        {
            urls.Add(new SitemapUrl { Loc = $"{baseUrl}/{branch}/ozel-ders", ChangeFreq = "daily", Priority = 0.8 });
        }

        // 4. Karma (Şehir+Branş) Dizin Sayfaları (Sadece Aktif İlan Olanlar)
        var activeCombinations = await _context.Listings
            .Include(l => l.District).ThenInclude(d => d.City)
            .Include(l => l.Branch)
            .Where(l => l.Status == ListingStatus.Active)
            .Select(l => new { CitySlug = l.District.City.Slug, BranchSlug = l.Branch.Slug })
            .Distinct()
            .ToListAsync();

        foreach (var combo in activeCombinations)
        {
            urls.Add(new SitemapUrl { Loc = $"{baseUrl}/{combo.CitySlug}/{combo.BranchSlug}-ozel-ders", ChangeFreq = "daily", Priority = 0.9 });
        }

        // 5. İlan Detay Sayfaları (Sadece Aktif)
        var listings = await _context.Listings
            .Where(l => l.Status == ListingStatus.Active)
            .Select(l => l.Slug)
            .ToListAsync();

        foreach (var slug in listings)
        {
            urls.Add(new SitemapUrl { Loc = $"{baseUrl}/ilan/{slug}", ChangeFreq = "daily", Priority = 0.8 });
        }

        // 6. XML Metnini Oluştur
        var xmlBuilder = new StringBuilder();
        xmlBuilder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xmlBuilder.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        
        foreach (var url in urls)
        {
            xmlBuilder.AppendLine("  <url>");
            xmlBuilder.AppendLine($"    <loc>{url.Loc}</loc>");
            xmlBuilder.AppendLine($"    <changefreq>{url.ChangeFreq}</changefreq>");
            xmlBuilder.AppendLine($"    <priority>{url.Priority:F2}</priority>");
            xmlBuilder.AppendLine("  </url>");
        }
        
        xmlBuilder.AppendLine("</urlset>");
        
        var xmlString = xmlBuilder.ToString();

        // 1 saat önbelleğe al
        await _cacheService.SetAsync(cacheKey, xmlString, TimeSpan.FromHours(1));

        return Content(xmlString, "application/xml", Encoding.UTF8);
    }
}

public class SitemapUrl
{
    public string Loc { get; set; } = string.Empty;
    public string ChangeFreq { get; set; } = "weekly";
    public double Priority { get; set; } = 0.5;
}
