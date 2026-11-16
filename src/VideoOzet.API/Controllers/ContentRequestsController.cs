using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
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
            Mod = IcerikModu.Klasik,
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
                Mod = (int)r.Mod,
                ModAdi = r.Mod.ToString(),
                VideoSayisi = r.SeriPlanlari.OrderByDescending(p => p.PlanNo).Select(p => (int?)p.VideoSayisi).FirstOrDefault(),
                // Seri taleplerinde ContentRequest.Durum planlama boyunca "Bekliyor" kalır;
                // gerçek ilerleme en güncel planın durumundadır.
                PlanDurum = r.SeriPlanlari.OrderByDescending(p => p.PlanNo).Select(p => (SeriPlanDurumu?)p.Durum).FirstOrDefault(),
                PlanNo = r.SeriPlanlari.OrderByDescending(p => p.PlanNo).Select(p => (int?)p.PlanNo).FirstOrDefault(),
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
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ISourceContextBuilder? _sourceContextBuilder;
    private readonly IQualityCheckService _qcService;

    public ContentRequestDetailsController(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        ISourceContextBuilder? sourceContextBuilder = null,
        IQualityCheckService? qcService = null)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _sourceContextBuilder = sourceContextBuilder;
        _qcService = qcService ?? new QualityCheckService(
            synthesisProvider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QualityCheckService>.Instance);
    }

    /// <summary>
    /// Talebin detayını, üretilen içeriği (RAG), QC sonuçlarını ve tüm versiyonlarını (V1, V2...) döner.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRequestDetail(Guid id)
    {
        var request = await _dbContext.ContentRequests
            .Include(r => r.GeneratedContent)
            .Include(r => r.Versions)
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
                KullanilanKaynakDetaylari = await LoadUsedSourceChunkDetailsAsync(request.EgitimId, request.GeneratedContent.KullanilanKaynaklar),
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

        // Versiyonları yükle
        var versionList = request.Versions.OrderBy(v => v.VersiyonNo).ToList();
        if (versionList.Count == 0 && request.GeneratedContent != null)
        {
            // Versiyon tablosunda henüz kayıt yoksa GeneratedContent'i V1 olarak sun
            versionList.Add(new ContentVersion
            {
                Id = request.GeneratedContent.Id,
                ContentRequestId = request.Id,
                VersiyonNo = 1,
                ArastirmaOzeti = request.GeneratedContent.ArastirmaOzeti,
                VideoPlani = request.GeneratedContent.VideoPlani,
                RevizeTalimati = "İlk Üretim (Orijinal Versiyon)",
                LlmModel = request.GeneratedContent.LlmModel,
                OlusturmaTarihi = request.GeneratedContent.OlusturmaTarihi,
                ToplamIddiaSayisi = request.QcResult?.ToplamIddiaSayisi,
                DesteklenenSayisi = request.QcResult?.DesteklenenSayisi,
                BelirsizSayisi = request.QcResult?.BelirsizSayisi,
                DesteklenmeyenSayisi = request.QcResult?.DesteklenmeyenSayisi,
                DetayliRapor = request.QcResult?.DetayliRapor,
                GuvenSkorYuzde = request.QcResult?.GuvenSkorYuzde,
                QcTarihi = request.QcResult?.OlusturmaTarihi
            });
        }

        dto.Versions = versionList.Select(v => new ContentVersionDto
        {
            Id = v.Id,
            VersiyonNo = v.VersiyonNo,
            ArastirmaOzeti = v.ArastirmaOzeti,
            VideoPlani = v.VideoPlani,
            RevizeTalimati = v.RevizeTalimati,
            LlmModel = v.LlmModel,
            OlusturmaTarihi = v.OlusturmaTarihi,
            ToplamIddiaSayisi = v.ToplamIddiaSayisi,
            DesteklenenSayisi = v.DesteklenenSayisi,
            BelirsizSayisi = v.BelirsizSayisi,
            DesteklenmeyenSayisi = v.DesteklenmeyenSayisi,
            DetayliRapor = v.DetayliRapor,
            GuvenSkorYuzde = v.GuvenSkorYuzde,
            QcTarihi = v.QcTarihi
        }).ToList();

        return Ok(dto);
    }

    /// <summary>
    /// Mevcut içerik üzerinde kullanıcının talimatına göre hedef odaklı revizyon yapar, yeni versiyon (V2, V3...) oluşturur ve versiyona özel QC çalıştırır.
    /// </summary>
    [HttpPost("{id:guid}/revise")]
    public async Task<IActionResult> ReviseContent(Guid id, [FromBody] ReviseContentRequestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.RevizeTalimati))
            return BadRequest(new { Mesaj = "Lütfen yapılması gereken revizyon talimatını belirtin." });

        var request = await _dbContext.ContentRequests
            .Include(r => r.GeneratedContent)
            .Include(r => r.Versions)
            .Include(r => r.QcResult)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request == null)
            return NotFound(new { Mesaj = "İçerik talebi bulunamadı." });

        if (request.GeneratedContent == null)
            return BadRequest(new { Mesaj = "Revize edilecek içerik henüz üretilmemiş." });

        // En son versiyonu bul
        var latestVersion = request.Versions.OrderByDescending(v => v.VersiyonNo).FirstOrDefault();
        var currentOzet = latestVersion?.ArastirmaOzeti ?? request.GeneratedContent.ArastirmaOzeti;
        var currentPlan = latestVersion?.VideoPlani ?? request.GeneratedContent.VideoPlani;
        var currentVersionNo = latestVersion?.VersiyonNo ?? 1;
        var nextVersionNo = currentVersionNo + 1;

        // Eğer V1 veritabanında henüz yoksa oluştur
        if (!request.Versions.Any(v => v.VersiyonNo == 1))
        {
            var v1 = new ContentVersion
            {
                ContentRequestId = request.Id,
                VersiyonNo = 1,
                ArastirmaOzeti = request.GeneratedContent.ArastirmaOzeti,
                VideoPlani = request.GeneratedContent.VideoPlani,
                RevizeTalimati = "İlk Üretim (Orijinal Versiyon)",
                LlmModel = request.GeneratedContent.LlmModel,
                OlusturmaTarihi = request.GeneratedContent.OlusturmaTarihi,
                ToplamIddiaSayisi = request.QcResult?.ToplamIddiaSayisi,
                DesteklenenSayisi = request.QcResult?.DesteklenenSayisi,
                BelirsizSayisi = request.QcResult?.BelirsizSayisi,
                DesteklenmeyenSayisi = request.QcResult?.DesteklenmeyenSayisi,
                DetayliRapor = request.QcResult?.DetayliRapor,
                GuvenSkorYuzde = request.QcResult?.GuvenSkorYuzde,
                QcTarihi = request.QcResult?.OlusturmaTarihi
            };
            _dbContext.ContentVersions.Add(v1);
        }

        var hedefAlan = (dto.HedefAlan ?? "hepsi").ToLowerInvariant();
        var revisedOzet = currentOzet;
        var revisedPlan = currentPlan;

        var revisionContext = string.Empty;
        if (_sourceContextBuilder != null)
        {
            try
            {
                var sc = await _sourceContextBuilder.BuildContextAsync(
                    request.EgitimId,
                    new[] { dto.RevizeTalimati, request.Konu }.Where(q => !string.IsNullOrWhiteSpace(q)),
                    120000,
                    ct);
                revisionContext = sc.ContextText;
            }
            catch { }
        }

        var topicWithThesis = string.IsNullOrWhiteSpace(request.EkTonTalimati)
            ? $"[YAZAR-TEZİ]: {request.Konu}"
            : $"[YAZAR-TEZİ]: {request.Konu} | {request.EkTonTalimati}";

        if (hedefAlan == "hepsi" || hedefAlan == "ozet")
        {
            revisedOzet = await _synthesisProvider.ReviseContentAsync(
                currentOzet,
                dto.RevizeTalimati,
                "Araştırma Özeti",
                topicWithThesis,
                revisionContext,
                ct);
        }

        if (hedefAlan == "hepsi" || hedefAlan == "plan")
        {
            revisedPlan = await _synthesisProvider.ReviseContentAsync(
                currentPlan,
                dto.RevizeTalimati,
                "Video Planı",
                topicWithThesis,
                revisionContext,
                ct);
        }

        var newVersion = new ContentVersion
        {
            ContentRequestId = request.Id,
            VersiyonNo = nextVersionNo,
            ArastirmaOzeti = revisedOzet,
            VideoPlani = revisedPlan,
            RevizeTalimati = dto.RevizeTalimati,
            LlmModel = string.IsNullOrWhiteSpace(_synthesisProvider.ActiveModelName) ? "bilinmiyor" : _synthesisProvider.ActiveModelName,
            OlusturmaTarihi = DateTime.UtcNow
        };

        // Yeni versiyon için otomatik Kalite Kontrolü (QC) çalıştır
        try
        {
            var prevReport = latestVersion?.DetayliRapor ?? request.QcResult?.DetayliRapor ?? "[]";
            var qc = await ExecuteQualityCheckInternal(request, revisedOzet, prevReport, ct, revisedPlan);
            newVersion.ToplamIddiaSayisi = qc.toplam;
            newVersion.DesteklenenSayisi = qc.desteklenen;
            newVersion.BelirsizSayisi = qc.belirsiz;
            newVersion.DesteklenmeyenSayisi = qc.desteklenmeyen;
            newVersion.DetayliRapor = qc.raporJson;
            newVersion.GuvenSkorYuzde = qc.guvenSkor;
            newVersion.QcTarihi = DateTime.UtcNow;
        }
        catch
        {
            // QC bir sebeple başarısız olursa versiyon kaydını bozma
            newVersion.DetayliRapor = "[]";
        }

        _dbContext.ContentVersions.Add(newVersion);

        // GeneratedContent'i de en güncel versiyonla senkronize et
        request.GeneratedContent.ArastirmaOzeti = revisedOzet;
        request.GeneratedContent.VideoPlani = revisedPlan;
        request.GeneratedContent.OlusturmaTarihi = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);

        return await GetRequestDetail(id);
    }

    /// <summary>
    /// Belirtilen versiyon (V1, V2, V3...) için Kalite Kontrolünü (QC) baştan çalıştırır ve günceller.
    /// Önceki rapordaki hata/uyarıların giderilip giderilmediğini ve kaynak uyumunu yeniden denetler.
    /// </summary>
    [HttpPost("{id:guid}/versions/{versiyonNo:int}/re-qc")]
    public async Task<IActionResult> ReRunQualityCheck(Guid id, int versiyonNo, CancellationToken ct)
    {
        var request = await _dbContext.ContentRequests
            .Include(r => r.GeneratedContent)
            .Include(r => r.Versions)
            .Include(r => r.QcResult)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request == null)
            return NotFound(new { Mesaj = "İçerik talebi bulunamadı." });

        var targetVersion = request.Versions.FirstOrDefault(v => v.VersiyonNo == versiyonNo);
        if (targetVersion == null)
        {
            if (versiyonNo == 1 && request.GeneratedContent != null)
            {
                targetVersion = new ContentVersion
                {
                    ContentRequestId = request.Id,
                    VersiyonNo = 1,
                    ArastirmaOzeti = request.GeneratedContent.ArastirmaOzeti,
                    VideoPlani = request.GeneratedContent.VideoPlani,
                    RevizeTalimati = "İlk Üretim (Orijinal Versiyon)",
                    LlmModel = request.GeneratedContent.LlmModel,
                    OlusturmaTarihi = request.GeneratedContent.OlusturmaTarihi
                };
                _dbContext.ContentVersions.Add(targetVersion);
            }
            else
            {
                return NotFound(new { Mesaj = $"Versiyon {versiyonNo} bulunamadı." });
            }
        }

        // Varsa bir önceki versiyonun QC raporunu al (V1 için önceki rapor gönderilmez, sıfırdan kaynaklarla doğrulanır)
        var prevVersion = request.Versions
            .Where(v => v.VersiyonNo < versiyonNo)
            .OrderByDescending(v => v.VersiyonNo)
            .FirstOrDefault();
        var prevReport = prevVersion?.DetayliRapor;

        var qc = await ExecuteQualityCheckInternal(request, targetVersion.ArastirmaOzeti, prevReport, ct, targetVersion.VideoPlani);
        targetVersion.ToplamIddiaSayisi = qc.toplam;
        targetVersion.DesteklenenSayisi = qc.desteklenen;
        targetVersion.BelirsizSayisi = qc.belirsiz;
        targetVersion.DesteklenmeyenSayisi = qc.desteklenmeyen;
        targetVersion.DetayliRapor = qc.raporJson;
        targetVersion.GuvenSkorYuzde = qc.guvenSkor;
        targetVersion.QcTarihi = DateTime.UtcNow;

        if (request.QcResult != null)
        {
            request.QcResult.ToplamIddiaSayisi = qc.toplam;
            request.QcResult.DesteklenenSayisi = qc.desteklenen;
            request.QcResult.BelirsizSayisi = qc.belirsiz;
            request.QcResult.DesteklenmeyenSayisi = qc.desteklenmeyen;
            request.QcResult.DetayliRapor = qc.raporJson;
            request.QcResult.GuvenSkorYuzde = qc.guvenSkor;
            request.QcResult.OlusturmaTarihi = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(ct);

        return await GetRequestDetail(id);
    }

    private async Task<(int toplam, int desteklenen, int belirsiz, int desteklenmeyen, string raporJson, decimal guvenSkor)> ExecuteQualityCheckInternal(
        ContentRequest request, 
        string contentText, 
        string? previousQcReportJson, 
        CancellationToken ct,
        string? videoPlanText = null)
    {
        var contextData = string.Empty;
        var chunkIdsStr = request.GeneratedContent?.KullanilanKaynaklar;
        List<Guid> chunkIds = new();

        if (!string.IsNullOrEmpty(chunkIdsStr))
        {
            try
            {
                if (chunkIdsStr.TrimStart().StartsWith("["))
                {
                    chunkIds = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(chunkIdsStr) ?? new();
                }
                else
                {
                    chunkIds = chunkIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
                }
            }
            catch { }
        }

        if (_sourceContextBuilder != null && chunkIds.Any())
        {
            var sc = await _sourceContextBuilder.BuildFromChunkIdsAsync(request.EgitimId, chunkIds, ct);
            contextData = sc.ContextText;
        }

        if (string.IsNullOrWhiteSpace(contextData) && chunkIds.Any())
        {
            var chunks = await _dbContext.VideoChunkDocuments
                .Where(c => chunkIds.Contains(c.Id))
                .Take(15)
                .ToListAsync(ct);

            var sb = new System.Text.StringBuilder();
            foreach (var chunk in chunks)
            {
                sb.AppendLine($"[Zaman={chunk.StartTimeMs / 1000.0}-{chunk.EndTimeMs / 1000.0}s]");
                sb.AppendLine(chunk.Text);
            }
            contextData = sb.ToString();
        }

        if (string.IsNullOrWhiteSpace(contextData))
        {
            var fallbackChunks = await _dbContext.VideoChunkDocuments
                .Where(c => c.EgitimId == request.EgitimId)
                .OrderBy(c => c.StartTimeMs)
                .Take(15)
                .ToListAsync(ct);

            if (fallbackChunks.Any())
            {
                var sb = new System.Text.StringBuilder();
                foreach (var chunk in fallbackChunks)
                {
                    sb.AppendLine($"[Referans Kaynak]");
                    sb.AppendLine(chunk.Text);
                }
                contextData = sb.ToString();
            }
        }

        if (string.IsNullOrWhiteSpace(contextData))
        {
            contextData = $"[YAZAR-TEZİ VE ODAK BAĞLAMI]: {request.Konu}";
        }

        var effectivePrevReport = string.IsNullOrWhiteSpace(previousQcReportJson) || previousQcReportJson == "[]"
            ? null
            : previousQcReportJson;

        var authorThesis = string.IsNullOrWhiteSpace(request.EkTonTalimati)
            ? request.Konu
            : $"{request.Konu} | {request.EkTonTalimati}";

        var qcResult = await _qcService.EvaluateAsync(
            summaryText: contentText,
            sourceContext: contextData,
            authorThesisContext: authorThesis,
            claimCount: 8,
            previousReportJson: effectivePrevReport,
            videoPlanText: videoPlanText,
            ct: ct);

        return (
            qcResult.ToplamIddiaSayisi,
            qcResult.DesteklenenSayisi,
            qcResult.BelirsizSayisi,
            qcResult.DesteklenmeyenSayisi,
            qcResult.DetayliRaporJson,
            qcResult.GuvenSkorYuzde);
    }

    private async Task<List<UsedSourceChunkDto>> LoadUsedSourceChunkDetailsAsync(Guid egitimId, string? kullanilanKaynaklarJson)
    {
        var details = new List<UsedSourceChunkDto>();
        if (string.IsNullOrWhiteSpace(kullanilanKaynaklarJson))
            return details;

        List<Guid> chunkIds;
        try
        {
            chunkIds = kullanilanKaynaklarJson.TrimStart().StartsWith("[")
                ? System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(kullanilanKaynaklarJson) ?? new()
                : kullanilanKaynaklarJson.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
        }
        catch
        {
            return details;
        }

        var distinctIds = chunkIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinctIds.Count == 0)
            return details;

        var chunks = await _dbContext.VideoChunkDocuments
            .AsNoTracking()
            .Where(c => c.EgitimId == egitimId && distinctIds.Contains(c.Id))
            .ToListAsync();

        var chunkMap = chunks.ToDictionary(c => c.Id);
        var videoIds = chunks.Where(c => c.VideoId.HasValue).Select(c => c.VideoId!.Value).Distinct().ToList();
        var docIds = chunks.Where(c => c.DokumanId.HasValue).Select(c => c.DokumanId!.Value).Distinct().ToList();

        var videoInfos = videoIds.Count > 0
            ? await _dbContext.Videolar
                .AsNoTracking()
                .Where(v => videoIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Baslik, v.KullanimTuru, OrijinalDil = v.Summary != null ? v.Summary.OrijinalDil : null })
                .ToDictionaryAsync(v => v.Id, v => (v.Baslik, v.KullanimTuru, v.OrijinalDil))
            : new Dictionary<Guid, (string Baslik, KaynakKullanimTuru KullanimTuru, string? OrijinalDil)>();

        var docInfos = docIds.Count > 0
            ? await _dbContext.Dokumanlar
                .AsNoTracking()
                .Where(d => docIds.Contains(d.Id))
                .Select(d => new { d.Id, d.DosyaAdi, d.KullanimTuru, OrijinalDil = d.DokumanMetin != null ? d.DokumanMetin.OrijinalDil : null })
                .ToDictionaryAsync(d => d.Id, d => (d.DosyaAdi, d.KullanimTuru, d.OrijinalDil))
            : new Dictionary<Guid, (string DosyaAdi, KaynakKullanimTuru KullanimTuru, string? OrijinalDil)>();

        for (int i = 0; i < distinctIds.Count; i++)
        {
            if (!chunkMap.TryGetValue(distinctIds[i], out var c))
                continue;

            (string Baslik, KaynakKullanimTuru KullanimTuru, string? OrijinalDil) vInfo = default;
            (string DosyaAdi, KaynakKullanimTuru KullanimTuru, string? OrijinalDil) dInfo = default;
            var hasVideo = c.VideoId.HasValue && videoInfos.TryGetValue(c.VideoId.Value, out vInfo);
            var hasDoc = c.DokumanId.HasValue && docInfos.TryGetValue(c.DokumanId.Value, out dInfo);

            string title = hasDoc && !string.IsNullOrWhiteSpace(dInfo.DosyaAdi)
                ? dInfo.DosyaAdi
                : (hasVideo && !string.IsNullOrWhiteSpace(vInfo.Baslik)
                    ? vInfo.Baslik
                    : (!string.IsNullOrWhiteSpace(c.KaynakBaslik) ? c.KaynakBaslik : "Bilinmeyen Kaynak"));

            // İndekste kalmış eski metin/değer yerine veritabanındaki güncel video/dokuman.KullanimTuru değerini baz al
            var liveKullanimTuru = hasVideo
                ? vInfo.KullanimTuru
                : (hasDoc ? dInfo.KullanimTuru : c.KullanimTuru);

            var liveLang = !string.IsNullOrWhiteSpace(c.KaynakDil)
                ? c.KaynakDil
                : (hasVideo && !string.IsNullOrWhiteSpace(vInfo.OrijinalDil)
                    ? vInfo.OrijinalDil!
                    : (hasDoc && !string.IsNullOrWhiteSpace(dInfo.OrijinalDil) ? dInfo.OrijinalDil! : "tr"));

            string? timeRange = null;
            if (c.EndTimeMs > c.StartTimeMs)
            {
                var tsS = TimeSpan.FromMilliseconds(c.StartTimeMs);
                var tsE = TimeSpan.FromMilliseconds(c.EndTimeMs);
                timeRange = $"{(int)tsS.TotalMinutes:D2}:{tsS.Seconds:D2}–{(int)tsE.TotalMinutes:D2}:{tsE.Seconds:D2}";
            }

            details.Add(new UsedSourceChunkDto
            {
                KanitKodu = $"K{details.Count + 1}",
                ChunkId = c.Id,
                KaynakTipi = c.VideoId.HasValue ? "Video" : "Dokuman",
                KaynakBaslik = title,
                BolumBasligi = c.BolumBasligi,
                ZamanAraligi = timeRange,
                KaynakDil = liveLang,
                KullanimTuru = liveKullanimTuru == KaynakKullanimTuru.SadeceArgumanVeMantik ? "DisKaynak" : "DogrudanReferans"
            });
        }

        return details;
    }
}
