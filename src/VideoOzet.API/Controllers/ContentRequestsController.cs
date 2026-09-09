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

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/egitimler/{egitimId:guid}/content-requests")]
public class ContentRequestsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;

    public ContentRequestsController(AppDbContext dbContext, IPublishEndpoint publishEndpoint)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
    }

    /// <summary>
    /// Belirli bir eğitim için RAG destekli yeni bir içerik talebi oluşturur.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateContentRequest(Guid egitimId, [FromBody] CreateContentRequestDto requestDto)
    {
        var egitimExists = await _dbContext.Egitimler.AnyAsync(e => e.Id == egitimId);
        if (!egitimExists)
            return NotFound(new { Mesaj = "Eğitim bulunamadı." });

        var contentRequest = new ContentRequest
        {
            EgitimId = egitimId,
            Konu = requestDto.Konu,
            HedefUzunluk = requestDto.HedefUzunluk,
            HedefKitle = requestDto.HedefKitle,
            Durum = ContentRequestDurumu.Bekliyor,
            OlusturmaTarihi = DateTime.UtcNow
        };

        _dbContext.ContentRequests.Add(contentRequest);
        await _dbContext.SaveChangesAsync();

        // RabbitMQ'ya gönder
        await _publishEndpoint.Publish(new ContentRequestedEvent
        {
            ContentRequestId = contentRequest.Id,
            EgitimId = egitimId,
            Konu = contentRequest.Konu,
            HedefUzunluk = contentRequest.HedefUzunluk,
            HedefKitle = contentRequest.HedefKitle
        });

        return Accepted(new { Mesaj = "İçerik talebi alındı ve işleniyor.", ContentRequestId = contentRequest.Id });
    }

    /// <summary>
    /// Eğitime ait geçmiş içerik taleplerini listeler.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRequests(Guid egitimId)
    {
        var requests = await _dbContext.ContentRequests
            .Where(r => r.EgitimId == egitimId)
            .OrderByDescending(r => r.OlusturmaTarihi)
            .Select(r => new
            {
                r.Id,
                r.Konu,
                Durum = r.Durum.ToString(),
                r.OlusturmaTarihi
            })
            .ToListAsync();

        return Ok(requests);
    }
}

[ApiController]
[Route("api/content-requests")]
public class ContentRequestDetailsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ContentRequestDetailsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Talebin detayını, üretilen içeriği (RAG) ve QC sonuçlarını döner.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRequestDetail(Guid id)
    {
        var request = await _dbContext.ContentRequests
            .Include(r => r.GeneratedContent)
            .Include(r => r.QcResult)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null)
            return NotFound(new { Mesaj = "İçerik talebi bulunamadı." });

        var dto = new ContentRequestDto
        {
            Id = request.Id,
            EgitimId = request.EgitimId,
            Konu = request.Konu,
            HedefUzunluk = request.HedefUzunluk,
            HedefKitle = request.HedefKitle,
            Durum = request.Durum,
            OlusturmaTarihi = request.OlusturmaTarihi,
            TamamlanmaTarihi = request.TamamlanmaTarihi
        };

        if (request.GeneratedContent != null)
        {
            dto.GeneratedContent = new GeneratedContentDto
            {
                Id = request.GeneratedContent.Id,
                ArastirmaOzeti = request.GeneratedContent.ArastirmaOzeti,
                VideoPlani = request.GeneratedContent.VideoPlani,
                KullanilanKaynaklar = request.GeneratedContent.KullanilanKaynaklar,
                LlmModel = request.GeneratedContent.LlmModel,
                TokenKullanimi = request.GeneratedContent.TokenKullanimi,
                UretimSuresiMs = request.GeneratedContent.UretimSuresiMs,
                OlusturmaTarihi = request.GeneratedContent.OlusturmaTarihi
            };
        }

        if (request.QcResult != null)
        {
            dto.QcResult = new QcResultDto
            {
                Id = request.QcResult.Id,
                ToplamIddiaSayisi = request.QcResult.ToplamIddiaSayisi,
                DesteklenenSayisi = request.QcResult.DesteklenenSayisi,
                BelirsizSayisi = request.QcResult.BelirsizSayisi,
                DesteklenmeyenSayisi = request.QcResult.DesteklenmeyenSayisi,
                DetayliRapor = request.QcResult.DetayliRapor,
                GuvenSkorYuzde = request.QcResult.GuvenSkorYuzde,
                OlusturmaTarihi = request.QcResult.OlusturmaTarihi
            };
        }

        return Ok(dto);
    }
}
