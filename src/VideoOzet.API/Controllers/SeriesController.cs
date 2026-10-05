using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Events;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using System.Text.Json;

namespace VideoOzet.API.Controllers;

[ApiController]
public class SeriesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;

    public SeriesController(AppDbContext dbContext, IPublishEndpoint publishEndpoint)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
    }

    /// <summary>
    /// Akıllı Seri Planlama (v3) akışını başlatır.
    /// Konu analizini tetikler (Map-Reduce).
    /// </summary>
    [HttpPost("api/egitimler/{egitimId:guid}/content-plans")]
    public async Task<IActionResult> CreateContentPlan(Guid egitimId, [FromBody] CreateSeriesRequestDto requestDto)
    {
        var egitimExists = await _dbContext.Egitimler.AnyAsync(e => e.Id == egitimId);
        if (!egitimExists)
            return NotFound(new { Mesaj = "Eğitim bulunamadı." });

        var contentRequest = new ContentRequest
        {
            EgitimId = egitimId,
            Konu = requestDto.Odak,
            HedefKitle = requestDto.HedefKitle,
            EkTonTalimati = requestDto.EkTonTalimati,
            Mod = IcerikModu.Planli, // Yeni mod
            Durum = ContentRequestDurumu.Bekliyor,
            OlusturmaTarihi = DateTime.UtcNow
        };

        _dbContext.ContentRequests.Add(contentRequest);
        await _dbContext.SaveChangesAsync();

        // TopicAnalysisConsumer'ı tetikler
        await _publishEndpoint.Publish(new TopicAnalysisRequestedEvent
        {
            ContentRequestId = contentRequest.Id,
            EgitimId = egitimId
        });

        return Accepted(new { Mesaj = "Analiz başladı.", ContentRequestId = contentRequest.Id });
    }

    /// <summary>
    /// İçerik talebinin mevcut plan durumunu ve konu analizini döner.
    /// </summary>
    [HttpGet("api/content-plans/{id:guid}")]
    public async Task<IActionResult> GetContentPlan(Guid id)
    {
        var request = await _dbContext.ContentRequests
            .Include(x => x.KonuAnalizi)
            .Include(x => x.SeriPlanlari)
                .ThenInclude(x => x.SeriBolumler)
                    .ThenInclude(x => x.Revizyonlar)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (request == null)
            return NotFound(new { Mesaj = "İçerik talebi bulunamadı." });

        var currentPlan = request.SeriPlanlari.OrderByDescending(x => x.PlanNo).FirstOrDefault();

        return Ok(new
        {
            request.Id,
            request.Durum,
            KonuAnalizi = request.KonuAnalizi,
            GuncelPlan = currentPlan
        });
    }

    /// <summary>
    /// Yeni kısıtlamalarla planı yeniden taslak olarak üretir.
    /// </summary>
    [HttpPost("api/content-plans/{id:guid}/plans/draft")]
    public async Task<IActionResult> GeneratePlanDraft(Guid id, [FromBody] SeriesDraftDto draftDto)
    {
        var request = await _dbContext.ContentRequests
            .Include(x => x.SeriPlanlari)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (request == null)
            return NotFound(new { Mesaj = "İçerik talebi bulunamadı." });

        var currentPlan = request.SeriPlanlari.OrderByDescending(x => x.PlanNo).FirstOrDefault();
        if (currentPlan == null)
            return BadRequest(new { Mesaj = "Öncelikle analiz tamamlanmalı." });

        if (currentPlan.Onaylandi)
        {
            // Plan onaylanmışsa yeni taslak oluşturmak yeni bir plan numarası açar.
            // Fakat v3 tasarımında, sayı değişmedikçe onaylı plan değişmez.
            // Sayı değiştiyse yeni plan açılır, bu SeriesPlanConsumer'da halledilebilir.
        }
        else
        {
            // Onaylanmamışsa üzerine yazar.
            currentPlan.KullaniciKisitlariJson = JsonSerializer.Serialize(draftDto);
            currentPlan.Durum = SeriPlanDurumu.Olusturuluyor;
            await _dbContext.SaveChangesAsync();
        }

        // SeriesPlanConsumer'ı tetikler
        await _publishEndpoint.Publish(new SeriesPlanRequestedEvent
        {
            ContentRequestId = request.Id,
            EgitimId = request.EgitimId
        });

        return Accepted(new { Mesaj = "Yeni plan taslağı oluşturuluyor." });
    }

    /// <summary>
    /// Planı onaylar ve 1. videonun üretimini tetikler.
    /// </summary>
    [HttpPost("api/series-requests/{id:guid}/plans/{planNo:int}/approve")]
    public async Task<IActionResult> ApprovePlan(Guid id, int planNo)
    {
        var plan = await _dbContext.SeriPlanlari
            .Include(p => p.SeriBolumler)
            .FirstOrDefaultAsync(p => p.ContentRequestId == id && p.PlanNo == planNo);

        if (plan == null)
            return NotFound(new { Mesaj = "Plan bulunamadı." });

        if (plan.Onaylandi)
            return BadRequest(new { Mesaj = "Bu plan zaten onaylanmış." });

        plan.Onaylandi = true;
        plan.Durum = SeriPlanDurumu.Onaylandi;

        var request = await _dbContext.ContentRequests.FindAsync(id);
        if (request != null)
        {
            request.Durum = ContentRequestDurumu.IcerikUretiliyor;
        }

        await _dbContext.SaveChangesAsync();

        var firstVideo = plan.SeriBolumler.OrderBy(b => b.BolumNo).FirstOrDefault();
        if (firstVideo != null)
        {
            firstVideo.Durum = BolumDurumu.Isleniyor;
            await _dbContext.SaveChangesAsync();

            await _publishEndpoint.Publish(new SeriesVideoGenerationCommand
            {
                ContentRequestId = id,
                EgitimId = request!.EgitimId,
                SeriPlaniId = plan.Id,
                SeriBolumId = firstVideo.Id,
                BolumNo = 1
            });
        }

        return Accepted(new { Mesaj = "Plan onaylandı, üretim başladı." });
    }

    /// <summary>
    /// Kullanıcının açıkça bir videoyu revize etme talebi.
    /// </summary>
    [HttpPost("api/series-requests/{id:guid}/videos/{bolumNo:int}/revisions")]
    public async Task<IActionResult> ReviseEpisode(Guid id, int bolumNo, [FromBody] EpisodeRevisionDto dto)
    {
        var bolum = await _dbContext.SeriBolumler
            .Include(b => b.SeriPlani)
                .ThenInclude(p => p.ContentRequest)
            .FirstOrDefaultAsync(b => b.SeriPlani.ContentRequestId == id && b.BolumNo == bolumNo);

        if (bolum == null)
            return NotFound(new { Mesaj = "Video bulunamadı." });

        if (bolum.Durum == BolumDurumu.Isleniyor)
            return Conflict(new { Mesaj = "Bu video şu anda zaten üretiliyor." });

        bolum.Durum = BolumDurumu.Isleniyor;
        await _dbContext.SaveChangesAsync();

        await _publishEndpoint.Publish(new SeriesVideoRevisionRequestedEvent
        {
            ContentRequestId = id,
            EgitimId = bolum.SeriPlani.ContentRequest.EgitimId,
            SeriBolumId = bolum.Id,
            Talimat = dto.Talimat
        });

        return Accepted(new { Mesaj = "Revizyon işlemi başlatıldı." });
    }
}
