using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
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
public class SeriesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ISourceTopicMapper? _sourceTopicMapper;
    private readonly ISourceContextBuilder? _sourceContextBuilder;
    private readonly IQualityCheckService? _qcService;
    private readonly ISynthesisProvider? _synthesisProvider;

    public SeriesController(
        AppDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        ISourceTopicMapper? sourceTopicMapper = null,
        ISourceContextBuilder? sourceContextBuilder = null,
        IQualityCheckService? qcService = null,
        ISynthesisProvider? synthesisProvider = null)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _sourceTopicMapper = sourceTopicMapper;
        _sourceContextBuilder = sourceContextBuilder;
        _qcService = qcService;
        _synthesisProvider = synthesisProvider;
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
    /// İçerik talebinin mevcut plan durumunu, konu analizini ve eğitim kaynak özet listesini döner.
    /// </summary>
    [HttpGet("api/content-plans/{id:guid}")]
    public async Task<IActionResult> GetContentPlan(Guid id, [FromQuery] int? planNo = null, CancellationToken ct = default)
    {
        var request = await _dbContext.ContentRequests
            .Include(x => x.KonuAnalizi)
            .Include(x => x.SeriPlanlari)
                .ThenInclude(x => x.SeriBolumler)
                    .ThenInclude(x => x.Revizyonlar)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (request == null)
            return NotFound(new { Mesaj = "İçerik talebi bulunamadı." });

        var orderedPlans = request.SeriPlanlari.OrderBy(x => x.PlanNo).ToList();
        var currentPlan = planNo.HasValue
            ? orderedPlans.FirstOrDefault(x => x.PlanNo == planNo.Value) ?? orderedPlans.LastOrDefault()
            : orderedPlans.LastOrDefault();

        var tumPlanlar = orderedPlans.Select(p => new
        {
            p.Id,
            p.PlanNo,
            p.VideoSayisi,
            p.Onaylandi,
            Durum = p.Durum.ToString(),
            TamamlananBolumSayisi = p.SeriBolumler.Count(b => b.Durum == BolumDurumu.Tamamlandi)
        }).ToList();

        var egitimKaynaklari = await LoadCourseSourceCardsAsync(request.EgitimId, ct);

        return Ok(new
        {
            request.Id,
            request.EgitimId,
            request.Konu,
            request.HedefKitle,
            request.EkTonTalimati,
            Durum = request.Durum.ToString(),
            TumPlanlar = tumPlanlar,
            EgitimKaynaklari = egitimKaynaklari,
            KonuAnalizi = request.KonuAnalizi == null ? null : new
            {
                request.KonuAnalizi.Id,
                request.KonuAnalizi.ContentRequestId,
                request.KonuAnalizi.AnaFikir,
                request.KonuAnalizi.BeklenenKonularJson,
                request.KonuAnalizi.KonuHaritasiJson,
                request.KonuAnalizi.KaynaktaOlmayanlarJson,
                request.KonuAnalizi.OnerilenVideoSayisi,
                request.KonuAnalizi.OneriSuresiDk,
                request.KonuAnalizi.OneriGerekcesi,
                Durum = request.KonuAnalizi.Durum.ToString(),
                request.KonuAnalizi.LlmModel
            },
            GuncelPlan = currentPlan == null ? null : new
            {
                currentPlan.Id,
                currentPlan.ContentRequestId,
                currentPlan.PlanNo,
                currentPlan.VideoSayisi,
                currentPlan.VarsayilanVideoSuresiDk,
                currentPlan.SeriHaritasiJson,
                currentPlan.DisaridaBirakilanlarJson,
                currentPlan.KullaniciKisitlariJson,
                currentPlan.OnayTalimati,
                currentPlan.OneridenFarkli,
                currentPlan.Onaylandi,
                Durum = currentPlan.Durum.ToString(),
                SeriBolumler = currentPlan.SeriBolumler.OrderBy(b => b.BolumNo).Select(b => new
                {
                    b.Id,
                    b.SeriPlaniId,
                    b.BolumNo,
                    b.CalismaBasligi,
                    b.Kanca,
                    b.AnaFikir,
                    b.HedefSureDk,
                    b.KonularJson,
                    b.SahneIskeletiJson,
                    b.AtananKaynaklarJson,
                    Durum = b.Durum.ToString(),
                    b.AktifRevizyonId,
                    b.BaglamEskidi,
                    b.BaglamSorunlariJson,
                    RevizyonSayisi = b.Revizyonlar.Count
                }).ToList()
            }
        });
    }

    /// <summary>
    /// Yeni kısıtlamalarla planı yeniden taslak olarak üretir.
    /// </summary>
    [HttpPost("api/content-plans/{id:guid}/plans/draft")]
    public async Task<IActionResult> GeneratePlanDraft(Guid id, [FromBody] SeriesDraftDto draftDto)
    {
        var request = await _dbContext.ContentRequests
            .Include(x => x.KonuAnalizi)
            .Include(x => x.SeriPlanlari)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (request == null)
            return NotFound(new { Mesaj = "İçerik talebi bulunamadı." });

        // Eğer konu analizi henüz tamamlanmamış veya hataya düşmüşse, konu analizinden itibaren yeniden başlat
        if (request.KonuAnalizi == null || request.KonuAnalizi.Durum != AnalizDurumu.Tamamlandi)
        {
            if (request.KonuAnalizi != null)
            {
                request.KonuAnalizi.Durum = AnalizDurumu.Isleniyor;
                await _dbContext.SaveChangesAsync();
            }

            await _publishEndpoint.Publish(new TopicAnalysisRequestedEvent
            {
                ContentRequestId = request.Id,
                EgitimId = request.EgitimId
            });

            return Accepted(new { Mesaj = "Konu analizi ve seri planı yeniden oluşturuluyor." });
        }

        var currentPlan = request.SeriPlanlari.OrderByDescending(x => x.PlanNo).FirstOrDefault();
        if (currentPlan == null)
        {
            currentPlan = new SeriPlani
            {
                ContentRequestId = request.Id,
                PlanNo = 1,
                KullaniciKisitlariJson = JsonSerializer.Serialize(draftDto),
                Durum = SeriPlanDurumu.Olusturuluyor,
                OneridenFarkli = false
            };
            _dbContext.SeriPlanlari.Add(currentPlan);
            await _dbContext.SaveChangesAsync();
        }
        else if (!currentPlan.Onaylandi)
        {
            // Yeni versiyon (PlanNo > 1) veya Hata durumundaki bir plan tekrar üretilirken önceki koruma bayrağını ve boşsa kayıtlı talimatı koru
            if (!string.IsNullOrWhiteSpace(currentPlan.KullaniciKisitlariJson))
            {
                try
                {
                    var prevDraft = JsonSerializer.Deserialize<SeriesDraftDto>(
                        currentPlan.KullaniciKisitlariJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (prevDraft != null)
                    {
                        if (currentPlan.PlanNo > 1 && prevDraft.MevcutMetinleriKoru)
                            draftDto.MevcutMetinleriKoru = true;

                        if (string.IsNullOrWhiteSpace(draftDto.Talimat) && string.IsNullOrWhiteSpace(draftDto.RevizyonTalimati))
                            draftDto.Talimat = prevDraft.RevizyonTalimati;
                    }
                }
                catch { }
            }

            currentPlan.KullaniciKisitlariJson = JsonSerializer.Serialize(draftDto);
            currentPlan.Durum = SeriPlanDurumu.Olusturuluyor;
            await _dbContext.SaveChangesAsync();
        }
        else
        {
            // Onaylanmış eski planı ve içindeki tüm dökümanları HİÇ SİLMEDEN yeni bir plan versiyonu (PlanNo + 1) aç!
            var nextPlan = new SeriPlani
            {
                Id = Guid.NewGuid(),
                ContentRequestId = request.Id,
                PlanNo = currentPlan.PlanNo + 1,
                KullaniciKisitlariJson = JsonSerializer.Serialize(draftDto),
                OnayTalimati = currentPlan.OnayTalimati ?? string.Empty,
                Durum = SeriPlanDurumu.Olusturuluyor,
                OneridenFarkli = true
            };
            _dbContext.SeriPlanlari.Add(nextPlan);
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
    public async Task<IActionResult> ApprovePlan(Guid id, int planNo, [FromBody] ApprovePlanDto? dto = null)
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

        if (!string.IsNullOrWhiteSpace(dto?.Talimat))
        {
            plan.OnayTalimati = dto.Talimat.Trim();
        }

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
                BolumNo = firstVideo.BolumNo
            });
        }

        return Accepted(new { Mesaj = "Plan onaylandı, üretim başladı." });
    }

    /// <summary>
    /// Hata alan veya yeniden üretilmek istenen bölümü baştan üretir.
    /// </summary>
    [HttpPost("api/series-requests/{id:guid}/videos/{bolumNo:int}/retry")]
    public async Task<IActionResult> RetryEpisode(Guid id, int bolumNo, [FromQuery] int? planNo = null)
    {
        var query = _dbContext.SeriBolumler
            .Include(b => b.Revizyonlar)
            .Include(b => b.SeriPlani)
                .ThenInclude(p => p.ContentRequest)
            .Where(b => b.SeriPlani.ContentRequestId == id && b.BolumNo == bolumNo);

        if (planNo.HasValue)
            query = query.Where(b => b.SeriPlani.PlanNo == planNo.Value);

        var bolum = await query
            .OrderByDescending(b => b.SeriPlani.PlanNo)
            .FirstOrDefaultAsync();

        if (bolum == null)
            return NotFound(new { Mesaj = "Video bulunamadı." });

        if (bolum.Durum == BolumDurumu.Isleniyor)
            return Conflict(new { Mesaj = "Bu video şu anda zaten üretiliyor." });

        bolum.Durum = BolumDurumu.Isleniyor;
        bolum.BaglamEskidi = false;
        bolum.BaglamSorunlariJson = "[]";

        var request = bolum.SeriPlani?.ContentRequest;
        if (request != null && request.Durum == ContentRequestDurumu.Hata)
        {
            request.Durum = ContentRequestDurumu.IcerikUretiliyor;
        }

        await _dbContext.SaveChangesAsync();

        await _publishEndpoint.Publish(new SeriesVideoGenerationCommand
        {
            ContentRequestId = id,
            EgitimId = bolum.SeriPlani!.ContentRequest.EgitimId,
            SeriPlaniId = bolum.SeriPlaniId,
            SeriBolumId = bolum.Id,
            BolumNo = bolum.BolumNo
        });

        return Accepted(new { Mesaj = "Bölüm üretimi yeniden başlatıldı." });
    }

    /// <summary>
    /// Kullanıcının açıkça bir videoyu revize etme talebi.
    /// </summary>
    [HttpPost("api/series-requests/{id:guid}/videos/{bolumNo:int}/revisions")]
    public async Task<IActionResult> ReviseEpisode(Guid id, int bolumNo, [FromBody] EpisodeRevisionDto dto, [FromQuery] int? planNo = null)
    {
        var query = _dbContext.SeriBolumler
            .Include(b => b.Revizyonlar)
            .Include(b => b.SeriPlani)
                .ThenInclude(p => p.ContentRequest)
            .Where(b => b.SeriPlani.ContentRequestId == id && b.BolumNo == bolumNo);

        if (planNo.HasValue)
            query = query.Where(b => b.SeriPlani.PlanNo == planNo.Value);

        var bolum = await query
            .OrderByDescending(b => b.SeriPlani.PlanNo)
            .FirstOrDefaultAsync();

        if (bolum == null)
            return NotFound(new { Mesaj = "Video bulunamadı." });

        if (bolum.Durum == BolumDurumu.Isleniyor)
            return Conflict(new { Mesaj = "Bu video şu anda zaten üretiliyor." });

        bolum.Durum = BolumDurumu.Isleniyor;
        await _dbContext.SaveChangesAsync();

        var isYenidenDene = string.IsNullOrWhiteSpace(dto?.Talimat) ||
            dto.Talimat.Trim().Equals("yeniden dene", StringComparison.OrdinalIgnoreCase) ||
            dto.Talimat.Trim().Equals("tekrar dene", StringComparison.OrdinalIgnoreCase) ||
            dto.Talimat.Trim().Equals("tekrar üret", StringComparison.OrdinalIgnoreCase);

        // Eğer bölüm henüz üretilememişse (veya Hata durumundaysa), hiç revizyonu yoksa veya talimat "yeniden dene" ise, sıfırdan üretim komutunu tetikle
        if (!bolum.Revizyonlar.Any() || isYenidenDene)
        {
            await _publishEndpoint.Publish(new SeriesVideoGenerationCommand
            {
                ContentRequestId = id,
                EgitimId = bolum.SeriPlani.ContentRequest.EgitimId,
                SeriPlaniId = bolum.SeriPlaniId,
                SeriBolumId = bolum.Id,
                BolumNo = bolum.BolumNo
            });

            return Accepted(new { Mesaj = "Bölüm üretimi yeniden başlatıldı." });
        }

        await _publishEndpoint.Publish(new SeriesVideoRevisionRequestedEvent
        {
            ContentRequestId = id,
            EgitimId = bolum.SeriPlani.ContentRequest.EgitimId,
            SeriBolumId = bolum.Id,
            Talimat = dto!.Talimat,
            HedefAlan = string.IsNullOrWhiteSpace(dto.HedefAlan) ? "Hepsi" : dto.HedefAlan,
            BazRevizyonNo = dto.BazRevizyonNo
        });

        return Accepted(new { Mesaj = "Revizyon işlemi başlatıldı." });
    }

    /// <summary>
    /// Harita üzerinde yapılan anlık bölüm düzenlemelerini (başlık, kanca, süre, ana fikir, konular, sahne iskeleti, atanan kaynaklar, yeni/silinen bölüm)
    /// doğrudan SeriBolumler tablosuna kaydeder ve BolumNo sıralamasını 1..N yeniden numaralandırır.
    /// </summary>
    [HttpPut("api/content-plans/{id:guid}/plans/{planNo:int}")]
    public async Task<IActionResult> UpdatePlanTexts(Guid id, int planNo, [FromBody] UpdatePlanDto? dto, CancellationToken ct = default)
    {
        if (dto == null || dto.Bolumler == null || dto.Bolumler.Count == 0)
            return BadRequest(new { Mesaj = "Güncellenecek en az bir bölüm bilgisi gönderilmelidir." });

        var plan = await _dbContext.SeriPlanlari
            .Include(p => p.SeriBolumler)
            .FirstOrDefaultAsync(p => p.ContentRequestId == id && p.PlanNo == planNo, ct);

        if (plan == null)
            return NotFound(new { Mesaj = "Plan bulunamadı." });

        if (plan.Onaylandi)
            return BadRequest(new { Mesaj = "Onaylanmış plan doğrudan düzenlenemez." });

        var existingEpisodes = plan.SeriBolumler.OrderBy(b => b.BolumNo).ToList();
        var incomingEpisodes = dto.Bolumler;

        int defaultDuration = plan.VarsayilanVideoSuresiDk > 0 ? plan.VarsayilanVideoSuresiDk : 15;

        // Fazla eski bölümleri sil (sondan başlayarak)
        if (existingEpisodes.Count > incomingEpisodes.Count)
        {
            var toRemove = existingEpisodes.Skip(incomingEpisodes.Count).ToList();
            foreach (var rem in toRemove)
            {
                plan.SeriBolumler.Remove(rem);
                _dbContext.SeriBolumler.Remove(rem);
            }
        }

        // Mevcut bölümleri güncelle ve yeni eklenenleri Guid.NewGuid() (.ValueGeneratedNever() uyumlu) ile oluştur
        for (int i = 0; i < incomingEpisodes.Count; i++)
        {
            var item = incomingEpisodes[i];
            int newBolumNo = i + 1;

            var calismaBasligi = string.IsNullOrWhiteSpace(item.CalismaBasligi)
                ? $"Bölüm {newBolumNo}"
                : item.CalismaBasligi.Trim();
            var kanca = item.Kanca?.Trim() ?? string.Empty;
            var anaFikir = item.AnaFikir?.Trim() ?? string.Empty;
            var hedefSureDk = item.HedefSureDk > 0 ? item.HedefSureDk : defaultDuration;

            var konularList = (item.Konular ?? new List<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .ToList();

            var sahneList = (item.SahneIskeleti ?? new List<SeriesSceneOutlineItemDto>())
                .Select((s, idx) => new SeriesSceneOutlineItemDto
                {
                    SahneNo = idx + 1,
                    Baslik = string.IsNullOrWhiteSpace(s.Baslik) ? $"Sahne {idx + 1}" : s.Baslik.Trim(),
                    TahminiSureDk = s.TahminiSureDk > 0 ? s.TahminiSureDk : 3,
                    AciklamaVeArgumanlar = s.AciklamaVeArgumanlar?.Trim() ?? string.Empty,
                    AtananKaynakKodlari = s.AtananKaynakKodlari ?? new List<string>()
                })
                .ToList();

            var atananKaynaklarList = (item.AtananKaynakIdleri ?? new List<Guid>())
                .Where(g => g != Guid.Empty)
                .Distinct()
                .ToList();

            var konularJson = JsonSerializer.Serialize(konularList);
            var sahneIskeletiJson = JsonSerializer.Serialize(sahneList);
            var atananKaynaklarJson = JsonSerializer.Serialize(atananKaynaklarList);

            if (i < existingEpisodes.Count)
            {
                var existing = existingEpisodes[i];
                existing.BolumNo = newBolumNo;
                existing.CalismaBasligi = calismaBasligi;
                existing.Kanca = kanca;
                existing.AnaFikir = anaFikir;
                existing.HedefSureDk = hedefSureDk;
                existing.KonularJson = konularJson;
                existing.SahneIskeletiJson = sahneIskeletiJson;
                existing.AtananKaynaklarJson = atananKaynaklarJson;
            }
            else
            {
                var yeniBolum = new SeriBolum
                {
                    Id = Guid.NewGuid(),
                    SeriPlaniId = plan.Id,
                    BolumNo = newBolumNo,
                    CalismaBasligi = calismaBasligi,
                    Kanca = kanca,
                    AnaFikir = anaFikir,
                    HedefSureDk = hedefSureDk,
                    KonularJson = konularJson,
                    SahneIskeletiJson = sahneIskeletiJson,
                    AtananKaynaklarJson = atananKaynaklarJson,
                    Durum = BolumDurumu.Bekliyor
                };
                plan.SeriBolumler.Add(yeniBolum);
                _dbContext.SeriBolumler.Add(yeniBolum);
            }
        }

        plan.VideoSayisi = incomingEpisodes.Count;

        // SeriHaritasiJson alanını da güncel SeriBolumler ile senkronize tut (Diff-Aware plan revizyonu için)
        var syncedMap = plan.SeriBolumler
            .OrderBy(b => b.BolumNo)
            .Select(b => new
            {
                b.BolumNo,
                b.CalismaBasligi,
                b.Kanca,
                b.AnaFikir,
                b.HedefSureDk,
                Konular = SafeDeserializeStringList(b.KonularJson),
                SahneIskeleti = SafeDeserializeScenes(b.SahneIskeletiJson),
                AtananKaynakIdleri = SafeDeserializeGuidList(b.AtananKaynaklarJson)
            })
            .ToList();
        plan.SeriHaritasiJson = JsonSerializer.Serialize(syncedMap);

        await _dbContext.SaveChangesAsync(ct);

        var updatedBolumler = plan.SeriBolumler
            .OrderBy(b => b.BolumNo)
            .Select(b => new
            {
                b.Id,
                b.SeriPlaniId,
                b.BolumNo,
                b.CalismaBasligi,
                b.Kanca,
                b.AnaFikir,
                b.HedefSureDk,
                b.KonularJson,
                b.SahneIskeletiJson,
                b.AtananKaynaklarJson,
                Durum = b.Durum.ToString(),
                b.AktifRevizyonId,
                b.BaglamEskidi,
                b.BaglamSorunlariJson,
                RevizyonSayisi = b.Revizyonlar?.Count ?? 0
            })
            .ToList();

        return Ok(new
        {
            Mesaj = "Plan güncellendi.",
            plan.Id,
            plan.ContentRequestId,
            plan.PlanNo,
            plan.VideoSayisi,
            plan.VarsayilanVideoSuresiDk,
            plan.OnayTalimati,
            plan.Onaylandi,
            Durum = plan.Durum.ToString(),
            SeriBolumler = updatedBolumler
        });
    }

    /// <summary>
    /// Belirli bir videonun detayını, tüm revizyonlarını ve çözümlenmiş kaynak kanıtlarını (KullanilanKaynakDetaylari) döner.
    /// </summary>
    [HttpGet("api/series-requests/{id:guid}/plans/{planNo:int}/videos/{bolumNo:int}")]
    public async Task<IActionResult> GetEpisodeDetails(Guid id, int planNo, int bolumNo, CancellationToken ct = default)
    {
        var bolum = await _dbContext.SeriBolumler
            .Include(b => b.SeriPlani)
                .ThenInclude(p => p.ContentRequest)
            .Include(b => b.Revizyonlar)
            .FirstOrDefaultAsync(
                b => b.SeriPlani.ContentRequestId == id && b.SeriPlani.PlanNo == planNo && b.BolumNo == bolumNo,
                ct);

        if (bolum == null)
            return NotFound(new { Mesaj = "Video bulunamadı." });

        return Ok(await BuildEpisodeDetailsResponseAsync(bolum, ct));
    }

    public record ManualEditEpisodeRevisionDto(string? ArastirmaOzeti, string? VideoPlani);

    /// <summary>
    /// Bölüm revizyonunun Araştırma Özeti ve/veya Video Planı metnini manuel olarak günceller.
    /// </summary>
    [HttpPut("api/series-requests/{id:guid}/videos/{bolumNo:int}/revisions/{revizyonNo:int}")]
    public async Task<IActionResult> UpdateEpisodeRevisionTexts(
        Guid id,
        int bolumNo,
        int revizyonNo,
        [FromBody] ManualEditEpisodeRevisionDto dto,
        [FromQuery] int? planNo = null,
        CancellationToken ct = default)
    {
        if (dto == null || (dto.ArastirmaOzeti == null && dto.VideoPlani == null))
            return BadRequest(new { Mesaj = "Güncellenecek metin bulunamadı." });

        var query = _dbContext.SeriBolumler
            .Include(b => b.Revizyonlar)
            .Include(b => b.SeriPlani)
            .Where(b => b.SeriPlani.ContentRequestId == id && b.BolumNo == bolumNo);

        if (planNo.HasValue)
            query = query.Where(b => b.SeriPlani.PlanNo == planNo.Value);

        var bolum = await query
            .OrderByDescending(b => b.SeriPlani.PlanNo)
            .FirstOrDefaultAsync(ct);

        if (bolum == null)
            return NotFound(new { Mesaj = "Bölüm bulunamadı." });

        var targetRev = bolum.Revizyonlar.FirstOrDefault(r => r.RevizyonNo == revizyonNo)
            ?? bolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault();

        if (targetRev == null)
            return NotFound(new { Mesaj = $"Bölüm {bolumNo} için revizyon {revizyonNo} bulunamadı." });

        if (dto.ArastirmaOzeti != null)
            targetRev.ArastirmaOzeti = dto.ArastirmaOzeti;

        if (dto.VideoPlani != null)
            targetRev.VideoPlani = dto.VideoPlani;

        // ÖNEMLİ KURAL: Döküman düzenlemesi ASLA plan tablosunu (SeriBolum) değiştirmez; Plan yalnızca plan ekranından değiştirilir.
        bolum.AktifRevizyonId = targetRev.Id;
        await _dbContext.SaveChangesAsync(ct);

        return Ok(new
        {
            mesaj = "Bölüm metni başarıyla güncellendi.",
            revizyonNo = targetRev.RevizyonNo,
            arastirmaOzeti = targetRev.ArastirmaOzeti,
            videoPlani = targetRev.VideoPlani
        });
    }

    /// <summary>
    /// Belirli bir bölüm revizyonu için Kalite Kontrolünü (Re-QC) hem Araştırma Özeti hem Video Planı
    /// ve Yazar Tezi bağlamıyla yeniden çalıştırır.
    /// </summary>
    [HttpPost("api/series-requests/{id:guid}/videos/{bolumNo:int}/revisions/{revizyonNo:int}/re-qc")]
    public async Task<IActionResult> ReRunEpisodeQualityCheck(
        Guid id,
        int bolumNo,
        int revizyonNo,
        CancellationToken ct = default)
    {
        var bolum = await _dbContext.SeriBolumler
            .Include(b => b.Revizyonlar)
            .Include(b => b.SeriPlani)
                .ThenInclude(p => p.ContentRequest)
            .Where(b => b.SeriPlani.ContentRequestId == id && b.BolumNo == bolumNo)
            .OrderByDescending(b => b.SeriPlani.PlanNo)
            .FirstOrDefaultAsync(ct);

        if (bolum == null)
            return NotFound(new { Mesaj = "Video bulunamadı." });

        var targetRev = bolum.Revizyonlar.FirstOrDefault(r => r.RevizyonNo == revizyonNo)
            ?? bolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault();

        if (targetRev == null)
            return NotFound(new { Mesaj = $"Bölüm {bolumNo} için revizyon {revizyonNo} bulunamadı." });

        var effectiveQcService = _qcService ?? (_synthesisProvider != null
            ? new QualityCheckService(
                _synthesisProvider,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<QualityCheckService>.Instance)
            : null);

        if (effectiveQcService == null)
            return StatusCode(500, new { Mesaj = "Kalite kontrol servisi yapılandırılmamış." });

        var request = bolum.SeriPlani?.ContentRequest;
        var egitimId = request?.EgitimId ?? Guid.Empty;

        var chunkIds = SafeDeserializeGuidList(targetRev.KullanilanKaynaklar);
        var assignedIds = SafeDeserializeGuidList(bolum.AtananKaynaklarJson);
        string contextData = string.Empty;

        if (_sourceContextBuilder != null && chunkIds.Count > 0 && egitimId != Guid.Empty)
        {
            var sc = await _sourceContextBuilder.BuildFromChunkIdsAsync(egitimId, chunkIds, ct);
            contextData = sc.ContextText;
        }

        if (string.IsNullOrWhiteSpace(contextData) && _sourceContextBuilder != null && egitimId != Guid.Empty)
        {
            var queries = new List<string> { bolum.CalismaBasligi, bolum.AnaFikir };
            queries.AddRange(SafeDeserializeStringList(bolum.KonularJson));
            queries = queries.Where(q => !string.IsNullOrWhiteSpace(q)).Distinct().ToList();

            var sc = await _sourceContextBuilder.BuildContextAsync(
                egitimId,
                queries,
                maxChars: 120000,
                preferredSourceIds: assignedIds.Count > 0 ? assignedIds : null,
                targetChunksOverride: null,
                ct: ct);
            contextData = sc.ContextText;
        }

        if (string.IsNullOrWhiteSpace(contextData) && chunkIds.Count > 0)
        {
            var dbChunks = await _dbContext.VideoChunkDocuments
                .AsNoTracking()
                .Where(c => chunkIds.Contains(c.Id))
                .Take(30)
                .ToListAsync(ct);

            if (dbChunks.Count > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < dbChunks.Count; i++)
                {
                    var c = dbChunks[i];
                    sb.AppendLine($"[K{i + 1}] ({c.KaynakBaslik})");
                    sb.AppendLine(c.Text);
                }
                contextData = sb.ToString();
            }
        }

        if (string.IsNullOrWhiteSpace(contextData) && !string.IsNullOrWhiteSpace(request?.Konu))
        {
            contextData = $"[YAZAR-TEZİ VE ODAK BAĞLAMI]: {request.Konu}";
        }

        var prevRevision = bolum.Revizyonlar
            .Where(r => r.RevizyonNo < targetRev.RevizyonNo && !string.IsNullOrWhiteSpace(r.DetayliRapor) && r.DetayliRapor != "[]")
            .OrderByDescending(r => r.RevizyonNo)
            .FirstOrDefault();

        var authorThesisParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request?.Konu))
            authorThesisParts.Add(request.Konu.Trim());
        if (!string.IsNullOrWhiteSpace(request?.EkTonTalimati))
            authorThesisParts.Add(request.EkTonTalimati.Trim());
        if (!string.IsNullOrWhiteSpace(bolum.SeriPlani?.OnayTalimati))
            authorThesisParts.Add(bolum.SeriPlani.OnayTalimati.Trim());
        if (!string.IsNullOrWhiteSpace(bolum.AnaFikir))
            authorThesisParts.Add($"Bölüm Ana Fikri: {bolum.AnaFikir.Trim()}");
        if (!string.IsNullOrWhiteSpace(targetRev.Talimat))
            authorThesisParts.Add($"Revizyon Talimatı: {targetRev.Talimat.Trim()}");

        var authorThesisContext = authorThesisParts.Count > 0
            ? string.Join(" | ", authorThesisParts)
            : bolum.CalismaBasligi;

        var qcResult = await effectiveQcService.EvaluateAsync(
            summaryText: targetRev.ArastirmaOzeti,
            sourceContext: contextData,
            authorThesisContext: authorThesisContext,
            claimCount: 8,
            previousReportJson: prevRevision?.DetayliRapor,
            videoPlanText: targetRev.VideoPlani,
            ct: ct);

        targetRev.GuvenSkorYuzde = qcResult.GuvenSkorYuzde;
        targetRev.ToplamIddiaSayisi = qcResult.ToplamIddiaSayisi;
        targetRev.DesteklenenSayisi = qcResult.DesteklenenSayisi;
        targetRev.BelirsizSayisi = qcResult.BelirsizSayisi;
        targetRev.DesteklenmeyenSayisi = qcResult.DesteklenmeyenSayisi;
        targetRev.DetayliRapor = qcResult.DetayliRaporJson;
        targetRev.QcDurumu = qcResult.Durum;

        await _dbContext.SaveChangesAsync(ct);

        var episodePayload = await BuildEpisodeDetailsResponseAsync(bolum, ct);
        return Ok(episodePayload);
    }

    private async Task<object> BuildEpisodeDetailsResponseAsync(SeriBolum bolum, CancellationToken ct)
    {
        var egitimId = bolum.SeriPlani?.ContentRequest?.EgitimId ?? Guid.Empty;
        if (egitimId == Guid.Empty && bolum.SeriPlani != null)
        {
            egitimId = await _dbContext.ContentRequests
                .AsNoTracking()
                .Where(r => r.Id == bolum.SeriPlani.ContentRequestId)
                .Select(r => r.EgitimId)
                .FirstOrDefaultAsync(ct);
        }

        var orderedRevisions = bolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).ToList();
        var allChunkIds = orderedRevisions
            .SelectMany(r => SafeDeserializeGuidList(r.KullanilanKaynaklar))
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToList();

        var chunkLookup = new Dictionary<Guid, VideoChunkDocument>();
        var videoInfos = new Dictionary<Guid, (string Baslik, KaynakKullanimTuru KullanimTuru, string? OrijinalDil)>();
        var docInfos = new Dictionary<Guid, (string DosyaAdi, KaynakKullanimTuru KullanimTuru, string? OrijinalDil)>();

        if (allChunkIds.Count > 0)
        {
            var chunks = await _dbContext.VideoChunkDocuments
                .AsNoTracking()
                .Where(c => allChunkIds.Contains(c.Id))
                .ToListAsync(ct);

            chunkLookup = chunks.ToDictionary(c => c.Id);

            var videoIds = chunks.Where(c => c.VideoId.HasValue).Select(c => c.VideoId!.Value).Distinct().ToList();
            var docIds = chunks.Where(c => c.DokumanId.HasValue).Select(c => c.DokumanId!.Value).Distinct().ToList();

            if (videoIds.Count > 0)
            {
                var videos = await _dbContext.Videolar
                    .AsNoTracking()
                    .Where(v => videoIds.Contains(v.Id))
                    .Select(v => new { v.Id, v.Baslik, v.KullanimTuru, OrijinalDil = v.Summary != null ? v.Summary.OrijinalDil : null })
                    .ToListAsync(ct);
                videoInfos = videos.ToDictionary(v => v.Id, v => (v.Baslik, v.KullanimTuru, v.OrijinalDil));
            }

            if (docIds.Count > 0)
            {
                var docs = await _dbContext.Dokumanlar
                    .AsNoTracking()
                    .Where(d => docIds.Contains(d.Id))
                    .Select(d => new { d.Id, d.DosyaAdi, d.KullanimTuru, OrijinalDil = d.DokumanMetin != null ? d.DokumanMetin.OrijinalDil : null })
                    .ToListAsync(ct);
                docInfos = docs.ToDictionary(d => d.Id, d => (d.DosyaAdi, d.KullanimTuru, d.OrijinalDil));
            }
        }

        var revisionDtos = orderedRevisions.Select(r =>
        {
            var revChunkIds = SafeDeserializeGuidList(r.KullanilanKaynaklar);
            var details = new List<UsedSourceChunkDto>();

            foreach (var cId in revChunkIds)
            {
                if (!chunkLookup.TryGetValue(cId, out var c))
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
                    KullanimTuru = liveKullanimTuru == KaynakKullanimTuru.SadeceArgumanVeMantik
                        ? "DisKaynak"
                        : "DogrudanReferans"
                });
            }

            return new
            {
                r.Id,
                r.SeriBolumId,
                r.RevizyonNo,
                Tip = r.Tip.ToString(),
                r.Talimat,
                r.HedefAlan,
                r.ArastirmaOzeti,
                r.VideoPlani,
                r.DevirNotuJson,
                r.BaglamJson,
                r.KullanilanKaynaklar,
                KullanilanKaynakDetaylari = details,
                r.GuvenSkorYuzde,
                r.ToplamIddiaSayisi,
                r.DesteklenenSayisi,
                r.BelirsizSayisi,
                r.DesteklenmeyenSayisi,
                DetayliRapor = r.DetayliRapor,
                QcDurumu = r.QcDurumu.ToString(),
                r.LlmModel,
                Durum = r.Durum.ToString()
            };
        }).ToList();

        return new
        {
            bolum.Id,
            bolum.SeriPlaniId,
            bolum.BolumNo,
            bolum.CalismaBasligi,
            bolum.Kanca,
            bolum.AnaFikir,
            bolum.HedefSureDk,
            bolum.KonularJson,
            bolum.SahneIskeletiJson,
            bolum.AtananKaynaklarJson,
            Durum = bolum.Durum.ToString(),
            bolum.AktifRevizyonId,
            bolum.BaglamEskidi,
            bolum.BaglamSorunlariJson,
            Revizyonlar = revisionDtos
        };
    }

    private async Task<List<CourseSourceCardDto>> LoadCourseSourceCardsAsync(Guid egitimId, CancellationToken ct)
    {
        var result = new List<CourseSourceCardDto>();
        if (egitimId == Guid.Empty)
            return result;

        // GET görüntüleme uçlarında LLM konu çıkarımıyla bloklanmamak için doğrudan DB ve mevcut önbellekten oku
        var videos = await _dbContext.Videolar
            .AsNoTracking()
            .Include(v => v.Summary)
            .Where(v => v.EgitimId == egitimId)
            .OrderBy(v => v.Sira)
            .ThenBy(v => v.OlusturmaTarihi)
            .ToListAsync(ct);

        var docs = await _dbContext.Dokumanlar
            .AsNoTracking()
            .Include(d => d.DokumanMetin)
            .Where(d => d.EgitimId == egitimId)
            .OrderBy(d => d.OlusturmaTarihi)
            .ToListAsync(ct);

        var allSourceIds = videos.Select(v => v.Id).Concat(docs.Select(d => d.Id)).ToList();
        var cachedTopics = allSourceIds.Count > 0
            ? await _dbContext.KaynakKonuCikarimlari
                .AsNoTracking()
                .Where(k => allSourceIds.Contains(k.KaynakId))
                .ToListAsync(ct)
            : new List<KaynakKonuCikarimi>();

        var existingById = result.ToDictionary(k => k.KaynakId);

        foreach (var v in videos)
        {
            var liveMode = v.KullanimTuru == KaynakKullanimTuru.SadeceArgumanVeMantik
                ? "SadeceArgumanVeMantik"
                : "DogrudanReferans";
            var lang = !string.IsNullOrWhiteSpace(v.Summary?.OrijinalDil) ? v.Summary.OrijinalDil : "tr";

            if (existingById.TryGetValue(v.Id, out var card))
            {
                card.Baslik = string.IsNullOrWhiteSpace(v.Baslik) ? card.Baslik : v.Baslik;
                card.KullanimTuru = liveMode;
                card.Dil = lang;
                continue;
            }

            var cached = cachedTopics.FirstOrDefault(c => c.KaynakId == v.Id);
            var topics = ExtractTopicTitlesFromCacheJson(cached?.KonularJson);
            var summarySnippet = v.Summary?.CevrilmisOzetMetni ?? v.Summary?.OzetMetni ?? string.Empty;
            if (summarySnippet.Length > 280)
                summarySnippet = summarySnippet[..280] + "...";

            var durationMinutes = v.Sure.HasValue
                ? Math.Max(1, (int)Math.Round(v.Sure.Value.TotalMinutes))
                : 5;

            var newCard = new CourseSourceCardDto(
                kaynakId: v.Id,
                kaynakTuru: "Video",
                baslik: v.Baslik,
                dil: lang,
                kullanimTuru: liveMode,
                tahminiToplamSureDk: durationMinutes,
                konular: topics,
                ozetBilgi: summarySnippet);

            result.Add(newCard);
            existingById[v.Id] = newCard;
        }

        foreach (var d in docs)
        {
            var liveMode = d.KullanimTuru == KaynakKullanimTuru.SadeceArgumanVeMantik
                ? "SadeceArgumanVeMantik"
                : "DogrudanReferans";
            var lang = !string.IsNullOrWhiteSpace(d.DokumanMetin?.OrijinalDil) ? d.DokumanMetin.OrijinalDil : "tr";

            if (existingById.TryGetValue(d.Id, out var card))
            {
                card.Baslik = string.IsNullOrWhiteSpace(d.DosyaAdi) ? card.Baslik : d.DosyaAdi;
                card.KullanimTuru = liveMode;
                card.Dil = lang;
                continue;
            }

            var cached = cachedTopics.FirstOrDefault(c => c.KaynakId == d.Id);
            var topics = ExtractTopicTitlesFromCacheJson(cached?.KonularJson);
            var summarySnippet = d.DokumanMetin?.CevrilmisOzetMetni ?? d.DokumanMetin?.DamitilmisOzetMetni ?? string.Empty;
            if (summarySnippet.Length > 280)
                summarySnippet = summarySnippet[..280] + "...";

            var newCard = new CourseSourceCardDto(
                kaynakId: d.Id,
                kaynakTuru: "Dokuman",
                baslik: d.DosyaAdi,
                dil: lang,
                kullanimTuru: liveMode,
                tahminiToplamSureDk: Math.Max(2, (d.DokumanMetin?.KelimeSayisi ?? 600) / 150),
                konular: topics,
                ozetBilgi: summarySnippet);

            result.Add(newCard);
            existingById[d.Id] = newCard;
        }

        return result;
    }

    private static List<string> ExtractTopicTitlesFromCacheJson(string? konularJson)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(konularJson))
            return list;

        try
        {
            using var doc = JsonDocument.Parse(konularJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (elem.ValueKind == JsonValueKind.String)
                    {
                        var val = elem.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            list.Add(val.Trim());
                    }
                    else if (elem.ValueKind == JsonValueKind.Object)
                    {
                        var konu = elem.TryGetProperty("Konu", out var kp) ? kp.GetString()
                            : (elem.TryGetProperty("konu", out kp) ? kp.GetString()
                            : (elem.TryGetProperty("Baslik", out kp) ? kp.GetString()
                            : (elem.TryGetProperty("baslik", out kp) ? kp.GetString() : null)));
                        if (!string.IsNullOrWhiteSpace(konu))
                            list.Add(konu.Trim());
                    }
                }
            }
        }
        catch { }

        return list;
    }

    private static List<string> SafeDeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed != null)
                return parsed.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }
        catch { }

        return new List<string>();
    }

    private static List<SeriesSceneOutlineItemDto> SafeDeserializeScenes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<SeriesSceneOutlineItemDto>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<SeriesSceneOutlineItemDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (parsed != null)
                return parsed;
        }
        catch { }

        return new List<SeriesSceneOutlineItemDto>();
    }

    /// <summary>
    /// Devam eden konu analizi, seri planlama veya bölüm üretim/revizyon sürecini durdurur.
    /// </summary>
    [HttpPost("api/content-plans/{id:guid}/cancel")]
    [HttpPost("api/series-requests/{id:guid}/cancel")]
    public async Task<IActionResult> CancelSeriesProcess(Guid id, CancellationToken ct = default)
    {
        var request = await _dbContext.ContentRequests
            .Include(r => r.KonuAnalizi)
            .Include(r => r.SeriPlanlari)
                .ThenInclude(p => p.SeriBolumler)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request == null)
            return NotFound(new { mesaj = "İçerik isteği bulunamadı." });

        if (request.Durum != ContentRequestDurumu.Tamamlandi)
            request.Durum = ContentRequestDurumu.Hata;

        if (request.KonuAnalizi != null &&
            (request.KonuAnalizi.Durum == AnalizDurumu.Bekliyor || request.KonuAnalizi.Durum == AnalizDurumu.Isleniyor))
        {
            request.KonuAnalizi.Durum = AnalizDurumu.Hata;
        }

        foreach (var plan in request.SeriPlanlari)
        {
            if (plan.Durum == SeriPlanDurumu.Taslak || plan.Durum == SeriPlanDurumu.Olusturuluyor)
                plan.Durum = SeriPlanDurumu.Iptal;

            foreach (var bolum in plan.SeriBolumler)
            {
                if (bolum.Durum == BolumDurumu.Bekliyor || bolum.Durum == BolumDurumu.Isleniyor)
                    bolum.Durum = BolumDurumu.Hata;
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        await _publishEndpoint.Publish(new PipelineProgressEvent
        {
            EgitimId = request.EgitimId,
            Asama = "Iptal",
            Durum = "Hata",
            Mesaj = "İşlem kullanıcı tarafından durduruldu."
        }, ct);

        return Ok(new { mesaj = "İşlem durduruldu.", contentRequestId = id });
    }

    private static List<Guid> SafeDeserializeGuidList(string? raw)
    {
        var list = new List<Guid>();
        if (string.IsNullOrWhiteSpace(raw))
            return list;

        var trimmed = raw.Trim();
        try
        {
            if (trimmed.StartsWith("["))
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.ValueKind == JsonValueKind.String && Guid.TryParse(elem.GetString(), out var g) && g != Guid.Empty)
                        {
                            list.Add(g);
                        }
                        else if (elem.ValueKind == JsonValueKind.Object)
                        {
                            var idStr = elem.TryGetProperty("kaynakId", out var p) ? p.GetString()
                                : (elem.TryGetProperty("KaynakId", out p) ? p.GetString()
                                : (elem.TryGetProperty("id", out p) ? p.GetString()
                                : (elem.TryGetProperty("Id", out p) ? p.GetString() : null)));
                            if (Guid.TryParse(idStr, out var objGuid) && objGuid != Guid.Empty)
                                list.Add(objGuid);
                        }
                    }
                }
            }
            else
            {
                foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Guid.TryParse(part.Trim(), out var g) && g != Guid.Empty)
                        list.Add(g);
                }
            }
        }
        catch { }

        return list.Distinct().ToList();
    }
}
