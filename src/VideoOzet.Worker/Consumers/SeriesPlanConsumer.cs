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
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Services;

namespace VideoOzet.Worker.Consumers;

public class SeriesPlanConsumer : IConsumer<SeriesPlanRequestedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ISourceTopicMapper _topicMapper;
    private readonly ISourceContextBuilder? _sourceContextBuilder;
    private readonly ILogService _logService;
    private readonly ILogger<SeriesPlanConsumer> _logger;

    public SeriesPlanConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        ISourceTopicMapper topicMapper,
        ILogService logService,
        ILogger<SeriesPlanConsumer> logger,
        ISourceContextBuilder? sourceContextBuilder = null)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _topicMapper = topicMapper;
        _logService = logService;
        _logger = logger;
        _sourceContextBuilder = sourceContextBuilder;
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

        Guid? activePlanId = null;

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
            activePlanId = seriPlani.Id;

            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = "SeriPlani",
                Durum = "Basladi",
                Mesaj = "Seri planı oluşturuluyor..."
            }, context.CancellationToken);

            // Kaynak bağlamı: Konu analizinde üretilen (ve KaynakKonuCikarimlari tablosunda önbelleklenen)
            // map-reduce konu özeti yeniden kullanılır. Ham doküman metinlerini (yüz binlerce karakter)
            // tek bir prompt'a gömmek LLM çağrısını dakikalarca uzatıyor ve bağlam limitlerini zorluyordu.
            var allSourcesData = await _topicMapper.BuildTopicDigestAsync(message.EgitimId, context.CancellationToken);
            
            var combinedSources = allSourcesData;
            if (_sourceContextBuilder != null)
            {
                var sourceContext = await _sourceContextBuilder.BuildContextAsync(
                    message.EgitimId,
                    new[] { request.Konu },
                    maxChars: 60000,
                    ct: context.CancellationToken);

                if (!string.IsNullOrWhiteSpace(sourceContext.ContextText))
                {
                    combinedSources = $"--- GERÇEK KAYNAK METİNLERİ ---\n{sourceContext.ContextText}\n\n--- KONU VE BAŞLIK DÖKÜMÜ ---\n{allSourcesData}";
                }
            }

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
                combinedSources,
                constraints,
                context.CancellationToken);
                
            var planData = LlmJson.Deserialize<System.Text.Json.JsonElement>(planJsonStr);
            if (planData.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new InvalidOperationException("Yapay zeka geçerli bir JSON objesi döndürmedi.");
            }

            seriPlani.VarsayilanVideoSuresiDk = ReadInt(planData, "VarsayilanVideoSuresiDk", 10);
            seriPlani.OneridenFarkli = seriPlani.OneridenFarkli || ReadBool(planData, "OneridenFarkli");
            seriPlani.DisaridaBirakilanlarJson = planData.TryGetProperty("DisaridaBirakilanlar", out var disarida) ? disarida.ToString() : "[]";
            seriPlani.Durum = SeriPlanDurumu.OnayBekliyor;

            var bolumSayaci = 0;
            if (planData.TryGetProperty("SeriHaritasi", out var harita) && harita.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                seriPlani.SeriHaritasiJson = harita.ToString();
                foreach (var b in harita.EnumerateArray())
                {
                    if (b.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

                    // BolumNo LLM'e bırakılmaz: (SeriPlaniId, BolumNo) benzersiz index'i tekrar eden/eksik
                    // numaralarda kaydı patlatır. Sıra, LLM'in döndürdüğü dizideki sıradır.
                    bolumSayaci++;

                    // Açık DbSet.Add: entity durumunu (Added) niyetle belirtir; navigation keşfine güvenmez.
                    _dbContext.SeriBolumler.Add(new SeriBolum
                    {
                        SeriPlaniId = seriPlani.Id,
                        BolumNo = bolumSayaci,
                        CalismaBasligi = ReadString(b, "CalismaBasligi"),
                        HedefSureDk = ReadInt(b, "HedefSureDk", seriPlani.VarsayilanVideoSuresiDk),
                        AnaFikir = ReadString(b, "AnaFikir"),
                        KonularJson = b.TryGetProperty("Konular", out var bkonu) ? bkonu.ToString() : "[]",
                        Durum = BolumDurumu.Bekliyor
                    });
                }
            }

            if (bolumSayaci == 0)
            {
                throw new InvalidOperationException("Yapay zeka seri haritasında hiç bölüm döndürmedi.");
            }

            seriPlani.VideoSayisi = bolumSayaci;
            
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

            // Hata yolu ASLA fırlatmamalı: fırlatırsa MassTransit tüm akışı (LLM çağrısı dahil) yeniden çalıştırır.
            await ConsumerFailureGuard.TryPersistFailureStateAsync(_dbContext, _logger, async (db, ct) =>
            {
                var plan = activePlanId.HasValue
                    ? await db.SeriPlanlari.FirstOrDefaultAsync(x => x.Id == activePlanId.Value, ct)
                    : await db.SeriPlanlari
                        .Where(x => x.ContentRequestId == request.Id)
                        .OrderByDescending(x => x.PlanNo)
                        .FirstOrDefaultAsync(ct);

                if (plan != null && !plan.Onaylandi)
                {
                    plan.Durum = SeriPlanDurumu.Hata;
                }
            });

            await ConsumerFailureGuard.TryRunAsync(_logger, "LogFunctionError",
                () => _logService.LogFunctionErrorAsync(nameof(SeriesPlanConsumer), ex, message));

            await ConsumerFailureGuard.TryRunAsync(_logger, "Publish ContentErrorEvent",
                () => context.Publish(new ContentErrorEvent
                {
                    ContentRequestId = request.Id,
                    EgitimId = request.EgitimId,
                    HataMesaji = ex.Message
                }, CancellationToken.None));
        }
    }

    // ---- LLM JSON çıktısı için toleranslı okuyucular ----
    // LLM'ler sayıları bazen "10" (string) veya 10.0 (ondalık) olarak döndürür; GetInt32() bu durumda fırlatır.

    private static int ReadInt(System.Text.Json.JsonElement obj, string name, int fallback)
    {
        if (!obj.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            System.Text.Json.JsonValueKind.Number when v.TryGetDouble(out var d) => (int)Math.Round(d),
            System.Text.Json.JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => fallback
        };
    }

    private static bool ReadBool(System.Text.Json.JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
            _ => false
        };
    }

    private static string ReadString(System.Text.Json.JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return string.Empty;
        return v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? string.Empty : v.ToString();
    }
}
