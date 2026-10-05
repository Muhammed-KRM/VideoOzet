using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Worker.Consumers;

public class SeriesPlanConsumer : IConsumer<SeriesPlanRequestedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ILogService _logService;
    private readonly ILogger<SeriesPlanConsumer> _logger;

    public SeriesPlanConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        ILogService logService,
        ILogger<SeriesPlanConsumer> logger)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _logService = logService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SeriesPlanRequestedEvent> context)
    {
        var message = context.Message;
        
        var request = await _dbContext.ContentRequests
            .Include(x => x.Egitim)
            .Include(x => x.KonuAnalizi)
            .FirstOrDefaultAsync(x => x.Id == message.ContentRequestId, context.CancellationToken);
            
        if (request == null || request.KonuAnalizi == null)
        {
            _logger.LogWarning("ContentRequest veya KonuAnalizi bulunamadı (Id: {Id}).", message.ContentRequestId);
            return;
        }

        try
        {
            var existingPlans = await _dbContext.SeriPlanlari
                .Include(p => p.SeriBolumler)
                .Where(p => p.ContentRequestId == request.Id)
                .OrderByDescending(p => p.PlanNo)
                .ToListAsync(context.CancellationToken);

            var latestPlan = existingPlans.FirstOrDefault();

            SeriPlani seriPlani;
            if (latestPlan == null)
            {
                seriPlani = new SeriPlani
                {
                    ContentRequestId = request.Id,
                    PlanNo = 1,
                    Durum = SeriPlanDurumu.Olusturuluyor,
                    OneridenFarkli = false
                };
                _dbContext.SeriPlanlari.Add(seriPlani);
            }
            else if (!latestPlan.Onaylandi)
            {
                // Onaylanmamışsa üzerine yazılır
                seriPlani = latestPlan;
                seriPlani.Durum = SeriPlanDurumu.Olusturuluyor;
                _dbContext.SeriBolumler.RemoveRange(seriPlani.SeriBolumler);
                seriPlani.SeriBolumler.Clear();
            }
            else
            {
                // Onaylıysa yeni plan açılır
                seriPlani = new SeriPlani
                {
                    ContentRequestId = request.Id,
                    PlanNo = latestPlan.PlanNo + 1,
                    Durum = SeriPlanDurumu.Olusturuluyor,
                    OneridenFarkli = false
                };
                _dbContext.SeriPlanlari.Add(seriPlani);
            }
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = "SeriPlani",
                Durum = "Basladi",
                Mesaj = "Seri planı oluşturuluyor..."
            }, context.CancellationToken);

            var contextBuilder = new StringBuilder();
            var videolar = await _dbContext.Videolar.Include(v => v.Summary).Where(v => v.EgitimId == message.EgitimId && v.Summary != null).ToListAsync(context.CancellationToken);
            var dokumanlar = await _dbContext.Dokumanlar.Include(d => d.DokumanMetin).Where(d => d.EgitimId == message.EgitimId && d.DokumanMetin != null).ToListAsync(context.CancellationToken);
            
            foreach (var video in videolar) { contextBuilder.AppendLine($"[VİDEO: {video.Baslik}]\n{video.Summary!.OzetMetni}\n"); }
            foreach (var doc in dokumanlar) { contextBuilder.AppendLine($"[DÖKÜMAN: {doc.DosyaAdi}]\n{doc.DokumanMetin!.HamMetin}\n"); }

            var allSourcesData = contextBuilder.ToString();
            
            var konuAnaliziJson = new
            {
                request.KonuAnalizi.AnaFikir,
                BeklenenKonular = LlmJson.Deserialize<string[]>(request.KonuAnalizi.BeklenenKonularJson),
                KonuHaritasi = LlmJson.Deserialize<dynamic>(request.KonuAnalizi.KonuHaritasiJson),
                KaynaktaOlmayanlar = LlmJson.Deserialize<string[]>(request.KonuAnalizi.KaynaktaOlmayanlarJson),
                request.KonuAnalizi.OnerilenVideoSayisi,
                request.KonuAnalizi.OneriSuresiDk
            };
            
            var kAnaliziStr = System.Text.Json.JsonSerializer.Serialize(konuAnaliziJson);

            var constraints = seriPlani.KullaniciKisitlariJson ?? "";
            if (!string.IsNullOrWhiteSpace(constraints) && constraints != "{}")
            {
                seriPlani.OneridenFarkli = true;
            }
            
            var planJsonStr = await _synthesisProvider.GenerateSeriesPlanAsync(
                request.Konu,
                request.HedefKitle ?? "Genel",
                kAnaliziStr,
                allSourcesData,
                constraints,
                context.CancellationToken);
                
            var planData = LlmJson.Deserialize<System.Text.Json.JsonElement>(planJsonStr);

            seriPlani.VideoSayisi = planData.TryGetProperty("VideoSayisi", out var videoSayisi) ? videoSayisi.GetInt32() : 1;
            seriPlani.VarsayilanVideoSuresiDk = planData.TryGetProperty("VarsayilanVideoSuresiDk", out var sure) ? sure.GetInt32() : 10;
            seriPlani.OneridenFarkli = seriPlani.OneridenFarkli || (planData.TryGetProperty("OneridenFarkli", out var farkli) && farkli.GetBoolean());
            seriPlani.DisaridaBirakilanlarJson = planData.TryGetProperty("DisaridaBirakilanlar", out var disarida) ? disarida.ToString() : "[]";
            seriPlani.Durum = SeriPlanDurumu.OnayBekliyor;
            
            if (planData.TryGetProperty("SeriHaritasi", out var harita) && harita.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                seriPlani.SeriHaritasiJson = harita.ToString();
                foreach (var b in harita.EnumerateArray())
                {
                    seriPlani.SeriBolumler.Add(new SeriBolum
                    {
                        BolumNo = b.TryGetProperty("BolumNo", out var bno) ? bno.GetInt32() : 1,
                        CalismaBasligi = b.TryGetProperty("CalismaBasligi", out var bbaslik) ? bbaslik.GetString() ?? "" : "",
                        HedefSureDk = b.TryGetProperty("HedefSureDk", out var bsure) ? bsure.GetInt32() : seriPlani.VarsayilanVideoSuresiDk,
                        AnaFikir = b.TryGetProperty("AnaFikir", out var bfikir) ? bfikir.GetString() ?? "" : "",
                        KonularJson = b.TryGetProperty("Konular", out var bkonu) ? bkonu.ToString() : "[]",
                        Durum = BolumDurumu.Bekliyor
                    });
                }
            }
            
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = "SeriPlani",
                Durum = "OnayBekliyor",
                Mesaj = "Plan oluşturuldu, kullanıcı onayı bekleniyor."
            }, context.CancellationToken);

            await context.Publish(new SeriesPlanGeneratedEvent
            {
                ContentRequestId = request.Id,
                EgitimId = request.EgitimId,
                SeriPlaniId = seriPlani.Id
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeriesPlanConsumer hata fırlattı: {Message}", ex.Message);
            await _logService.LogFunctionErrorAsync(nameof(SeriesPlanConsumer), ex, message);
            
            // Eğer plan eklendiyse durumu hata yap, eklenmediyse hata dön.
            var seriPlani = await _dbContext.SeriPlanlari
                .FirstOrDefaultAsync(x => x.ContentRequestId == request.Id, context.CancellationToken);
                
            if (seriPlani != null)
            {
                seriPlani.Durum = SeriPlanDurumu.Hata;
                await _dbContext.SaveChangesAsync(context.CancellationToken);
            }
            
            await context.Publish(new ContentErrorEvent
            {
                ContentRequestId = request.Id,
                EgitimId = request.EgitimId,
                HataMesaji = ex.Message
            }, context.CancellationToken);
        }
    }
}
