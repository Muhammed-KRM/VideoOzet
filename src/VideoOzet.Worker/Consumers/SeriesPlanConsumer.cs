using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
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
    private static readonly Regex GenericEpisodeTitleRegex = new(
        @"^\s*(bölüm|bolum|video|ders|part|episode|kısım|kisim)\s*\d+\s*([:.\-–—]\s*(bölüm|bolum|video|giriş|giris)?)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

            // Diff-Aware Güncelleme: Eski bölümler silinmeden ÖNCE mevcut taslağı serileştir!
            string previousPlanJson = string.Empty;
            if (latestPlan != null && latestPlan.SeriBolumler.Count > 0 && latestPlan.Durum != SeriPlanDurumu.Hata)
            {
                previousPlanJson = SerializeExistingPlanSnapshot(latestPlan);
            }

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
                seriPlani = latestPlan;
                seriPlani.Durum = SeriPlanDurumu.Olusturuluyor;
                _dbContext.SeriBolumler.RemoveRange(seriPlani.SeriBolumler);
                seriPlani.SeriBolumler.Clear();
            }
            else
            {
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
                Mesaj = "Akıllı seri haritası ve kaynak atamaları oluşturuluyor..."
            }, context.CancellationToken);

            // Yapılandırılmış Kaynak Kartları (%100 kaynak görünürlüğü; boş dönerse BuildTopicDigestAsync fallback)
            var structuredCards = await _topicMapper.GetStructuredSourceCardsAsync(message.EgitimId, context.CancellationToken);
            string structuredSourcesStr;
            if (structuredCards != null && structuredCards.Count > 0)
            {
                structuredSourcesStr = JsonSerializer.Serialize(structuredCards, new JsonSerializerOptions { WriteIndented = false });
            }
            else
            {
                structuredSourcesStr = await _topicMapper.BuildTopicDigestAsync(message.EgitimId, context.CancellationToken);
            }

            var combinedSources = structuredSourcesStr;
            if (_sourceContextBuilder != null)
            {
                var queries = new List<string> { request.Konu };
                if (!string.IsNullOrWhiteSpace(request.EkTonTalimati))
                    queries.Add(request.EkTonTalimati);

                var sourceContext = await _sourceContextBuilder.BuildContextAsync(
                    message.EgitimId,
                    queries,
                    maxChars: 55000,
                    ct: context.CancellationToken);

                if (sourceContext != null && !string.IsNullOrWhiteSpace(sourceContext.ContextText))
                {
                    combinedSources = $"--- EĞİTİM KAYNAK KARTLARI VE KONU DÖKÜMÜ ---\n{structuredSourcesStr}\n\n--- ÖRNEK GERÇEK KAYNAK METİNLERİ ---\n{sourceContext.ContextText}";
                }
            }

            var konuAnaliziJson = new
            {
                request.KonuAnalizi.AnaFikir,
                BeklenenKonular = SafeDeserializeArray(request.KonuAnalizi.BeklenenKonularJson),
                KonuHaritasi = SafeDeserializeDynamic(request.KonuAnalizi.KonuHaritasiJson),
                KaynaktaOlmayanlar = SafeDeserializeArray(request.KonuAnalizi.KaynaktaOlmayanlarJson),
                request.KonuAnalizi.OnerilenVideoSayisi,
                request.KonuAnalizi.OneriSuresiDk
            };

            var kAnaliziStr = JsonSerializer.Serialize(konuAnaliziJson);

            var rawConstraintsJson = seriPlani.KullaniciKisitlariJson ?? string.Empty;
            var userDraft = ParseUserDraftConstraints(rawConstraintsJson);
            var formattedConstraintsText = FormatUserConstraintsText(userDraft, rawConstraintsJson, seriPlani.OnayTalimati);

            if (HasAnyUserConstraint(userDraft, rawConstraintsJson))
            {
                seriPlani.OneridenFarkli = true;
            }

            var topicAndThesis = BuildTopicAndThesisHeader(request.Konu, request.EkTonTalimati, seriPlani.OnayTalimati);
            var targetAudience = request.HedefKitle ?? "Genel";

            // 7-parametreli (+ ct) fark-bazlı çağrı; Moq testlerinde yalnız 5-paramlı overload kuruluysa otomatik fallback!
            var planJsonStr = await CallGenerateSeriesPlanWithFallbackAsync(
                topicAndThesis,
                request.Konu,
                targetAudience,
                request.EkTonTalimati ?? string.Empty,
                kAnaliziStr,
                combinedSources,
                formattedConstraintsText,
                previousPlanJson,
                context.CancellationToken);

            var planData = LlmJson.Deserialize<JsonElement>(planJsonStr);
            if (planData.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Yapay zeka geçerli bir JSON objesi döndürmedi.");
            }

            int llmDefaultDuration = ReadInt(planData, "VarsayilanVideoSuresiDk", 10);
            seriPlani.VarsayilanVideoSuresiDk = userDraft?.VarsayilanSureDk is int uDef && uDef > 0
                ? uDef
                : llmDefaultDuration;
            seriPlani.OneridenFarkli = seriPlani.OneridenFarkli || ReadBool(planData, "OneridenFarkli");
            seriPlani.DisaridaBirakilanlarJson = planData.TryGetProperty("DisaridaBirakilanlar", out var disarida) ? disarida.ToString() : "[]";

            var episodes = ParseEpisodesFromPlanElement(planData, seriPlani.VarsayilanVideoSuresiDk);

            // Deterministik C# Kısıt Zorlayıcı (EnforcePlanConstraintsAsync)
            episodes = await EnforcePlanConstraintsAsync(
                episodes,
                userDraft,
                seriPlani.VarsayilanVideoSuresiDk,
                structuredCards,
                topicAndThesis,
                request.Konu,
                targetAudience,
                request.EkTonTalimati ?? string.Empty,
                kAnaliziStr,
                combinedSources,
                formattedConstraintsText,
                previousPlanJson,
                context.CancellationToken);

            if (episodes.Count == 0)
            {
                throw new InvalidOperationException("Yapay zeka seri haritasında hiç bölüm döndürmedi.");
            }

            seriPlani.Durum = SeriPlanDurumu.OnayBekliyor;
            seriPlani.VideoSayisi = episodes.Count;

            for (int idx = 0; idx < episodes.Count; idx++)
            {
                var ep = episodes[idx];
                int bolumNo = idx + 1;

                _dbContext.SeriBolumler.Add(new SeriBolum
                {
                    Id = Guid.NewGuid(),
                    SeriPlaniId = seriPlani.Id,
                    BolumNo = bolumNo,
                    CalismaBasligi = ep.CalismaBasligi,
                    Kanca = ep.Kanca,
                    HedefSureDk = ep.HedefSureDk,
                    AnaFikir = ep.AnaFikir,
                    KonularJson = JsonSerializer.Serialize(ep.Konular),
                    SahneIskeletiJson = JsonSerializer.Serialize(ep.SahneIskeleti),
                    AtananKaynaklarJson = JsonSerializer.Serialize(ep.AtananKaynakIdleri),
                    Durum = BolumDurumu.Bekliyor
                });
            }

            seriPlani.SeriHaritasiJson = JsonSerializer.Serialize(episodes.Select((ep, i) => new
            {
                BolumNo = i + 1,
                ep.CalismaBasligi,
                ep.Kanca,
                ep.HedefSureDk,
                ep.AnaFikir,
                ep.Konular,
                ep.SahneIskeleti,
                ep.AtananKaynakIdleri
            }));

            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = "SeriPlani",
                Durum = "OnayBekliyor",
                Mesaj = $"Plan oluşturuldu ({seriPlani.VideoSayisi} bölüm), kullanıcı onayı bekleniyor."
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

    private async Task<string> CallGenerateSeriesPlanWithFallbackAsync(
        string topicAndThesis,
        string rawTopic,
        string targetAudience,
        string toneInstruction,
        string konuAnaliziJson,
        string combinedSources,
        string formattedConstraintsText,
        string previousPlanJson,
        CancellationToken ct)
    {
        var richResult = await _synthesisProvider.GenerateSeriesPlanAsync(
            topicAndThesis,
            targetAudience,
            toneInstruction,
            konuAnaliziJson,
            combinedSources,
            formattedConstraintsText,
            previousPlanJson,
            ct);

        if (!string.IsNullOrWhiteSpace(richResult))
        {
            return richResult;
        }

        // Eski 5-parametreli ISynthesisProvider metodunu mocklayan birim testler için geriye uyumlu çağrı
        return await _synthesisProvider.GenerateSeriesPlanAsync(
            rawTopic,
            targetAudience,
            konuAnaliziJson,
            combinedSources,
            formattedConstraintsText,
            ct);
    }

    private async Task<List<ParsedEpisodeDraft>> EnforcePlanConstraintsAsync(
        List<ParsedEpisodeDraft> episodes,
        SeriesDraftDto? userDraft,
        int defaultDurationDk,
        IReadOnlyList<CourseSourceCardDto>? structuredCards,
        string topicAndThesis,
        string rawTopic,
        string targetAudience,
        string toneInstruction,
        string konuAnaliziJson,
        string combinedSources,
        string formattedConstraintsText,
        string previousPlanJson,
        CancellationToken ct)
    {
        if (episodes.Count == 0)
            return episodes;

        int? targetCount = userDraft?.VideoSayisi is int vc && vc > 0 ? vc : null;
        bool countMismatch = targetCount.HasValue && episodes.Count != targetCount.Value;
        bool hasGenericTitles = episodes.Any(e => IsGenericEpisodeTitle(e.CalismaBasligi));

        // 1. Bölüm sayısı ihlali veya jenerik "Bölüm X / Video X" başlığı varsa 1 adet LLM düzeltme çağrısı yap
        if (countMismatch || hasGenericTitles)
        {
            var violationNotes = new StringBuilder();
            violationNotes.AppendLine("⚠️ ÖNCEKİ ÇIKTIDA KISIT İHLALİ TESPİT EDİLDİ — DERHAL DÜZELT:");
            if (countMismatch)
            {
                violationNotes.AppendLine($"- Kullanıcı KESİN OLARAK {targetCount!.Value} ADET bölüm istedi, ancak sen {episodes.Count} adet bölüm ürettin! SeriHaritasi dizisi TAM OLARAK {targetCount.Value} elemanlı olmalıdır!");
            }
            if (hasGenericTitles)
            {
                violationNotes.AppendLine("- 'Bölüm 1', 'Video 1' gibi jenerik başlıklar KESİNLİKLE YASAKTIR! Her bölüm için içeriği anlatan özgün çalışma başlığı üret!");
            }
            if (!string.IsNullOrWhiteSpace(formattedConstraintsText))
            {
                violationNotes.AppendLine(formattedConstraintsText);
            }

            try
            {
                var currentInvalidSnapshot = JsonSerializer.Serialize(episodes);
                var repairedJsonStr = await CallGenerateSeriesPlanWithFallbackAsync(
                    topicAndThesis,
                    rawTopic,
                    targetAudience,
                    toneInstruction,
                    konuAnaliziJson,
                    combinedSources,
                    violationNotes.ToString(),
                    string.IsNullOrWhiteSpace(previousPlanJson) ? currentInvalidSnapshot : previousPlanJson,
                    ct);

                if (!string.IsNullOrWhiteSpace(repairedJsonStr))
                {
                    var repairedData = LlmJson.Deserialize<JsonElement>(repairedJsonStr);
                    if (repairedData.ValueKind == JsonValueKind.Object)
                    {
                        var repairedEpisodes = ParseEpisodesFromPlanElement(repairedData, defaultDurationDk);
                        if (repairedEpisodes.Count > 0)
                        {
                            episodes = repairedEpisodes;
                        }
                    }
                }
            }
            catch (Exception repEx) when (repEx is not OperationCanceledException)
            {
                _logger.LogWarning(repEx, "Plan kısıt düzeltme LLM çağrısı başarısız oldu, deterministik C# zorlayıcı devreye giriyor.");
            }
        }

        // 2. HaricKonular filtresi
        if (userDraft?.HaricKonular != null && userDraft.HaricKonular.Count > 0)
        {
            var excluded = userDraft.HaricKonular.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            if (excluded.Count > 0)
            {
                foreach (var ep in episodes)
                {
                    ep.Konular = ep.Konular
                        .Where(k => !excluded.Any(ex => k.Contains(ex, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }
            }
        }

        // 3. Deterministik VideoSayisi sabitleme (fazlaysa anlamca birleştir, eksikse böl)
        if (targetCount.HasValue && targetCount.Value > 0)
        {
            while (episodes.Count > targetCount.Value && episodes.Count >= 2)
            {
                var secondLast = episodes[episodes.Count - 2];
                var last = episodes[episodes.Count - 1];
                episodes[episodes.Count - 2] = MergeTwoEpisodes(secondLast, last);
                episodes.RemoveAt(episodes.Count - 1);
            }

            while (episodes.Count < targetCount.Value && episodes.Count > 0)
            {
                int splitIdx = episodes
                    .Select((ep, idx) => (ep, idx, score: ep.Konular.Count * 2 + ep.SahneIskeleti.Count + (ep.AnaFikir?.Length ?? 0) / 80))
                    .OrderByDescending(x => x.score)
                    .First().idx;

                var (firstHalf, secondHalf) = SplitEpisode(episodes[splitIdx], rawTopic, episodes.Count + 1);
                episodes[splitIdx] = firstHalf;
                episodes.Insert(splitIdx + 1, secondHalf);
            }
        }

        // 4. ZorunluKonular garantisi
        if (userDraft?.ZorunluKonular != null && userDraft.ZorunluKonular.Count > 0 && episodes.Count > 0)
        {
            foreach (var mustTopic in userDraft.ZorunluKonular.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()))
            {
                bool exists = episodes.Any(e =>
                    e.Konular.Any(k => k.Contains(mustTopic, StringComparison.OrdinalIgnoreCase)) ||
                    e.AnaFikir.Contains(mustTopic, StringComparison.OrdinalIgnoreCase) ||
                    e.CalismaBasligi.Contains(mustTopic, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    var lightestEp = episodes.OrderBy(e => e.Konular.Count).First();
                    lightestEp.Konular.Add(mustTopic);
                }
            }
        }

        // 5. Jenerik başlık düzeltme + VarsayilanSureDk & VideoSureleri zorlama + Kaynak atama tamamlama
        var validCardIds = structuredCards != null && structuredCards.Count > 0
            ? new HashSet<Guid>(structuredCards.Select(c => c.KaynakId))
            : null;

        for (int i = 0; i < episodes.Count; i++)
        {
            var ep = episodes[i];
            int bolumNo = i + 1;

            if (IsGenericEpisodeTitle(ep.CalismaBasligi))
            {
                ep.CalismaBasligi = DeriveMeaningfulEpisodeTitle(ep, rawTopic, bolumNo);
            }

            // Süre kısıtlarını kesin olarak uygula
            if (userDraft?.VideoSureleri != null &&
                userDraft.VideoSureleri.TryGetValue(bolumNo, out int specificDuration) &&
                specificDuration > 0)
            {
                ep.HedefSureDk = specificDuration;
            }
            else if (userDraft?.VarsayilanSureDk is int forcedDefault && forcedDefault > 0)
            {
                ep.HedefSureDk = forcedDefault;
            }
            else if (ep.HedefSureDk <= 0)
            {
                ep.HedefSureDk = defaultDurationDk > 0 ? defaultDurationDk : 10;
            }

            // Sahne iskeleti sürelerini HedefSureDk ile hizala
            NormalizeEpisodeSceneOutline(ep);

            // AtananKaynakIdleri doğrulama ve otomatik eşleştirme
            ep.AtananKaynakIdleri = ep.AtananKaynakIdleri.Where(id => id != Guid.Empty).Distinct().ToList();
            if (validCardIds != null)
            {
                ep.AtananKaynakIdleri = ep.AtananKaynakIdleri.Where(id => validCardIds.Contains(id)).ToList();

                if (ep.AtananKaynakIdleri.Count == 0 && structuredCards != null && structuredCards.Count > 0)
                {
                    ep.AtananKaynakIdleri = MatchCardsToEpisode(ep, structuredCards, i, episodes.Count);
                }
            }
        }

        return episodes;
    }

    public static bool IsGenericEpisodeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return true;

        return GenericEpisodeTitleRegex.IsMatch(title.Trim());
    }

    private static string DeriveMeaningfulEpisodeTitle(ParsedEpisodeDraft ep, string rawTopic, int bolumNo)
    {
        var firstTopic = ep.Konular.FirstOrDefault(k => !string.IsNullOrWhiteSpace(k) && !IsGenericEpisodeTitle(k));
        if (!string.IsNullOrWhiteSpace(firstTopic))
        {
            return firstTopic.Length > 95 ? firstTopic.Substring(0, 92).TrimEnd() + "..." : firstTopic.Trim();
        }

        var firstScene = ep.SahneIskeleti.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Baslik) && !IsGenericEpisodeTitle(s.Baslik));
        if (firstScene != null)
        {
            return firstScene.Baslik.Trim();
        }

        if (!string.IsNullOrWhiteSpace(ep.AnaFikir))
        {
            var firstClause = ep.AnaFikir.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstClause) && !IsGenericEpisodeTitle(firstClause))
            {
                return firstClause.Length > 95 ? firstClause.Substring(0, 92).TrimEnd() + "..." : firstClause;
            }
        }

        var baseTopic = string.IsNullOrWhiteSpace(rawTopic) ? "Konu Analizi" : rawTopic.Trim();
        if (baseTopic.Length > 60) baseTopic = baseTopic.Substring(0, 60).TrimEnd();
        return $"{baseTopic}: {bolumNo}. Analitik Perspektif ve Derin İnceleme";
    }

    private static ParsedEpisodeDraft MergeTwoEpisodes(ParsedEpisodeDraft a, ParsedEpisodeDraft b)
    {
        string mergedTitle = !IsGenericEpisodeTitle(a.CalismaBasligi) && !IsGenericEpisodeTitle(b.CalismaBasligi) &&
                             !string.Equals(a.CalismaBasligi, b.CalismaBasligi, StringComparison.OrdinalIgnoreCase)
            ? $"{a.CalismaBasligi} & {b.CalismaBasligi}"
            : (!IsGenericEpisodeTitle(a.CalismaBasligi) ? a.CalismaBasligi : b.CalismaBasligi);

        if (mergedTitle.Length > 140)
            mergedTitle = mergedTitle.Substring(0, 137).TrimEnd() + "...";

        var mergedScenes = a.SahneIskeleti.Concat(b.SahneIskeleti).ToList();
        for (int i = 0; i < mergedScenes.Count; i++)
            mergedScenes[i].SahneNo = i + 1;

        return new ParsedEpisodeDraft
        {
            CalismaBasligi = mergedTitle,
            Kanca = !string.IsNullOrWhiteSpace(a.Kanca) ? a.Kanca : b.Kanca,
            HedefSureDk = Math.Max(a.HedefSureDk, b.HedefSureDk),
            AnaFikir = $"{a.AnaFikir} {b.AnaFikir}".Trim(),
            Konular = a.Konular.Concat(b.Konular).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SahneIskeleti = mergedScenes,
            AtananKaynakIdleri = a.AtananKaynakIdleri.Concat(b.AtananKaynakIdleri).Distinct().ToList()
        };
    }

    private static (ParsedEpisodeDraft First, ParsedEpisodeDraft Second) SplitEpisode(ParsedEpisodeDraft src, string rawTopic, int nextNo)
    {
        int midTopics = Math.Max(1, src.Konular.Count / 2);
        var topicsA = src.Konular.Take(midTopics).ToList();
        var topicsB = src.Konular.Skip(midTopics).ToList();
        if (topicsB.Count == 0 && topicsA.Count > 0)
            topicsB.Add($"{topicsA[0]} – İleri Tartışma ve Sentez");

        int midScenes = Math.Max(1, src.SahneIskeleti.Count / 2);
        var scenesA = src.SahneIskeleti.Take(midScenes).ToList();
        var scenesB = src.SahneIskeleti.Skip(midScenes).ToList();

        var first = new ParsedEpisodeDraft
        {
            CalismaBasligi = src.CalismaBasligi,
            Kanca = src.Kanca,
            HedefSureDk = src.HedefSureDk,
            AnaFikir = src.AnaFikir,
            Konular = topicsA,
            SahneIskeleti = scenesA,
            AtananKaynakIdleri = new List<Guid>(src.AtananKaynakIdleri)
        };

        var secondTitle = topicsB.FirstOrDefault() ?? $"{src.CalismaBasligi} – Karşılaştırmalı Analiz ve Sentez";
        if (secondTitle.Length > 120) secondTitle = secondTitle.Substring(0, 117).TrimEnd() + "...";

        var second = new ParsedEpisodeDraft
        {
            CalismaBasligi = IsGenericEpisodeTitle(secondTitle) ? DeriveMeaningfulEpisodeTitle(first, rawTopic, nextNo) : secondTitle,
            Kanca = $"{secondTitle} ekseninde temel sorunun derinleştirilmesi",
            HedefSureDk = src.HedefSureDk,
            AnaFikir = src.AnaFikir,
            Konular = topicsB,
            SahneIskeleti = scenesB,
            AtananKaynakIdleri = new List<Guid>(src.AtananKaynakIdleri)
        };

        return (first, second);
    }

    private static void NormalizeEpisodeSceneOutline(ParsedEpisodeDraft ep)
    {
        if (ep.SahneIskeleti == null || ep.SahneIskeleti.Count == 0)
            return;

        int sum = ep.SahneIskeleti.Sum(s => Math.Max(1, s.TahminiSureDk));
        if (sum > 0 && ep.HedefSureDk > 0 && sum != ep.HedefSureDk && ep.SahneIskeleti.Count <= ep.HedefSureDk)
        {
            double ratio = (double)ep.HedefSureDk / sum;
            foreach (var s in ep.SahneIskeleti)
                s.TahminiSureDk = Math.Max(1, (int)Math.Round(Math.Max(1, s.TahminiSureDk) * ratio));

            int diff = ep.HedefSureDk - ep.SahneIskeleti.Sum(s => s.TahminiSureDk);
            int guard = 0;
            while (diff != 0 && guard < 100)
            {
                var target = ep.SahneIskeleti[guard % ep.SahneIskeleti.Count];
                if (diff > 0) { target.TahminiSureDk++; diff--; }
                else if (target.TahminiSureDk > 1) { target.TahminiSureDk--; diff++; }
                guard++;
            }
        }

        for (int i = 0; i < ep.SahneIskeleti.Count; i++)
        {
            ep.SahneIskeleti[i].SahneNo = i + 1;
        }
    }

    private static List<Guid> MatchCardsToEpisode(
        ParsedEpisodeDraft ep,
        IReadOnlyList<CourseSourceCardDto> cards,
        int episodeIndex,
        int totalEpisodes)
    {
        var epText = $"{ep.CalismaBasligi} {ep.AnaFikir} {string.Join(" ", ep.Konular)}".ToLowerInvariant();
        var words = Regex.Split(epText, @"\W+")
            .Where(w => w.Length >= 4)
            .Distinct()
            .ToList();

        var scored = cards
            .Select(card =>
            {
                var cardText = $"{card.Baslik} {string.Join(" ", card.Konular)} {card.OzetBilgi}".ToLowerInvariant();
                int hits = words.Count(w => cardText.Contains(w, StringComparison.Ordinal));
                return (card.KaynakId, hits);
            })
            .Where(x => x.hits > 0)
            .OrderByDescending(x => x.hits)
            .Take(Math.Max(4, (int)Math.Ceiling((double)cards.Count / Math.Max(1, totalEpisodes))))
            .Select(x => x.KaynakId)
            .ToList();

        if (scored.Count > 0)
            return scored;

        // Hiç kelime örtüşmesi bulunamadıysa kaynakları bölümlere dengeli dağıt
        int perEp = Math.Max(1, (int)Math.Ceiling((double)cards.Count / Math.Max(1, totalEpisodes)));
        return cards.Skip(episodeIndex * perEp).Take(perEp).Select(c => c.KaynakId).ToList();
    }

    private static string SerializeExistingPlanSnapshot(SeriPlani plan)
    {
        var ordered = plan.SeriBolumler.OrderBy(b => b.BolumNo).Select(b => new
        {
            b.BolumNo,
            b.CalismaBasligi,
            b.Kanca,
            b.HedefSureDk,
            b.AnaFikir,
            Konular = SafeDeserializeArray(b.KonularJson),
            SahneIskeleti = SafeDeserializeDynamic(b.SahneIskeletiJson),
            AtananKaynakIdleri = SafeDeserializeArray(b.AtananKaynaklarJson)
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            plan.PlanNo,
            plan.VideoSayisi,
            plan.VarsayilanVideoSuresiDk,
            SeriHaritasi = ordered
        }, new JsonSerializerOptions { WriteIndented = false });
    }

    private static SeriesDraftDto? ParseUserDraftConstraints(string? rawConstraintsJson)
    {
        if (string.IsNullOrWhiteSpace(rawConstraintsJson))
            return null;

        var trimmed = rawConstraintsJson.Trim();
        if (trimmed == "{}")
            return null;

        try
        {
            var jsonPart = LlmJson.ExtractJson(trimmed);
            if (!string.IsNullOrWhiteSpace(jsonPart) && jsonPart.StartsWith("{"))
            {
                var dto = JsonSerializer.Deserialize<SeriesDraftDto>(jsonPart, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                });

                // Ek tolerans: VideoSureleri string anahtarlı veya Talimat farklı büyük/küçük harfliyse tamamla
                using var doc = JsonDocument.Parse(jsonPart, new JsonDocumentOptions { AllowTrailingCommas = true });
                var root = doc.RootElement;
                if (dto != null && root.ValueKind == JsonValueKind.Object)
                {
                    if (string.IsNullOrWhiteSpace(dto.RevizyonTalimati))
                    {
                        foreach (var p in root.EnumerateObject())
                        {
                            if (string.Equals(p.Name, "Talimat", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(p.Name, "RevizyonTalimati", StringComparison.OrdinalIgnoreCase))
                            {
                                var val = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
                                if (!string.IsNullOrWhiteSpace(val))
                                    dto.Talimat = val;
                            }
                        }
                    }

                    if (dto.VideoSureleri.Count == 0)
                    {
                        foreach (var p in root.EnumerateObject())
                        {
                            if (string.Equals(p.Name, "VideoSureleri", StringComparison.OrdinalIgnoreCase) &&
                                p.Value.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var kv in p.Value.EnumerateObject())
                                {
                                    if (int.TryParse(kv.Name, out int epNo))
                                    {
                                        int dur = kv.Value.ValueKind == JsonValueKind.Number && kv.Value.TryGetInt32(out int iv)
                                            ? iv
                                            : (int.TryParse(kv.Value.ToString(), out int sv) ? sv : 0);
                                        if (dur > 0) dto.VideoSureleri[epNo] = dur;
                                    }
                                }
                            }
                        }
                    }
                }

                return dto;
            }
        }
        catch
        {
            return new SeriesDraftDto { Talimat = trimmed };
        }

        return new SeriesDraftDto { Talimat = trimmed };
    }

    private static bool HasAnyUserConstraint(SeriesDraftDto? dto, string rawJson)
    {
        if (dto == null)
            return !string.IsNullOrWhiteSpace(rawJson) && rawJson.Trim() != "{}";

        return (dto.VideoSayisi.HasValue && dto.VideoSayisi.Value > 0) ||
               (dto.VarsayilanSureDk.HasValue && dto.VarsayilanSureDk.Value > 0) ||
               dto.VideoSureleri.Count > 0 ||
               dto.ZorunluKonular.Count > 0 ||
               dto.HaricKonular.Count > 0 ||
               dto.KilitliAtamalar.Count > 0 ||
               !string.IsNullOrWhiteSpace(dto.RevizyonTalimati);
    }

    private static string FormatUserConstraintsText(SeriesDraftDto? dto, string rawJson, string? onayTalimati)
    {
        var sb = new StringBuilder();
        if (dto != null)
        {
            if (dto.VideoSayisi.HasValue && dto.VideoSayisi.Value > 0)
                sb.AppendLine($"- KESİN BÖLÜM SAYISI (VideoSayisi): TAM OLARAK {dto.VideoSayisi.Value} adet bölüm üretilecek! (Ne eksik ne fazla!)");
            if (dto.VarsayilanSureDk.HasValue && dto.VarsayilanSureDk.Value > 0)
                sb.AppendLine($"- VARSAYILAN BÖLÜM SÜRESİ (VarsayilanSureDk): Her bölüm {dto.VarsayilanSureDk.Value} dakika olacak.");
            if (dto.VideoSureleri != null && dto.VideoSureleri.Count > 0)
            {
                var pairs = dto.VideoSureleri.OrderBy(k => k.Key).Select(kv => $"{kv.Key}. Bölüm = {kv.Value} dk");
                sb.AppendLine($"- BÖLÜM BAZLI ÖZEL SÜRELER (VideoSureleri): {string.Join(", ", pairs)}");
            }
            if (dto.ZorunluKonular != null && dto.ZorunluKonular.Count > 0)
                sb.AppendLine($"- ZORUNLU İŞLENECEK KONULAR: {string.Join(" | ", dto.ZorunluKonular)}");
            if (dto.HaricKonular != null && dto.HaricKonular.Count > 0)
                sb.AppendLine($"- KESİNLİKLE HARİÇ TUTULACAK KONULAR (Plana dahil etme!): {string.Join(" | ", dto.HaricKonular)}");
            if (dto.KilitliAtamalar != null && dto.KilitliAtamalar.Count > 0)
            {
                var locks = dto.KilitliAtamalar.Select(kv => $"'{kv.Key}' -> Bölüm {kv.Value}");
                sb.AppendLine($"- KİLİTLİ KONU ATAMALARI: {string.Join(", ", locks)}");
            }
            if (!string.IsNullOrWhiteSpace(dto.RevizyonTalimati))
                sb.AppendLine($"- KULLANICI REVİZYON / KURGU TALİMATI: {dto.RevizyonTalimati.Trim()}");
        }
        else if (!string.IsNullOrWhiteSpace(rawJson) && rawJson.Trim() != "{}")
        {
            sb.AppendLine($"- KULLANICI KISITI: {rawJson.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(onayTalimati))
        {
            sb.AppendLine($"- PLAN ONAY DİREKTİFİ: {onayTalimati.Trim()}");
        }

        return sb.ToString().Trim();
    }

    private static string BuildTopicAndThesisHeader(string? konu, string? ekTon, string? onayTalimati)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.IsNullOrWhiteSpace(konu) ? "Genel Seri Konusu" : konu.Trim());
        if (!string.IsNullOrWhiteSpace(ekTon))
            sb.AppendLine($"[EK TON VE YAZAR PERSPEKTİFİ]: {ekTon.Trim()}");
        if (!string.IsNullOrWhiteSpace(onayTalimati))
            sb.AppendLine($"[ONAY NOTU]: {onayTalimati.Trim()}");
        return sb.ToString().Trim();
    }

    private static List<ParsedEpisodeDraft> ParseEpisodesFromPlanElement(JsonElement planData, int fallbackDurationDk)
    {
        var list = new List<ParsedEpisodeDraft>();
        if (!planData.TryGetProperty("SeriHaritasi", out var harita) || harita.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var b in harita.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object) continue;

            var topics = new List<string>();
            if (b.TryGetProperty("Konular", out var bkonu) && bkonu.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in bkonu.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) topics.Add(s.Trim());
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        var t = ReadString(item, "Baslik");
                        var a = ReadString(item, "Aciklama");
                        var combined = $"{t} {a}".Trim();
                        if (!string.IsNullOrWhiteSpace(combined)) topics.Add(combined);
                    }
                    else
                    {
                        var raw = item.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(raw)) topics.Add(raw);
                    }
                }
            }

            var scenes = new List<SeriesSceneOutlineItemDto>();
            if (b.TryGetProperty("SahneIskeleti", out var bsahne) && bsahne.ValueKind == JsonValueKind.Array)
            {
                int sNo = 1;
                foreach (var sc in bsahne.EnumerateArray())
                {
                    if (sc.ValueKind != JsonValueKind.Object) continue;
                    var kodlar = new List<string>();
                    if (sc.TryGetProperty("AtananKaynakKodlari", out var ak) && ak.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var k in ak.EnumerateArray())
                        {
                            var ks = k.ValueKind == JsonValueKind.String ? k.GetString() : k.ToString();
                            if (!string.IsNullOrWhiteSpace(ks)) kodlar.Add(ks.Trim());
                        }
                    }

                    scenes.Add(new SeriesSceneOutlineItemDto
                    {
                        SahneNo = ReadInt(sc, "SahneNo", sNo),
                        Baslik = ReadString(sc, "Baslik"),
                        TahminiSureDk = Math.Max(1, ReadInt(sc, "TahminiSureDk", 3)),
                        AciklamaVeArgumanlar = ReadString(sc, "AciklamaVeArgumanlar"),
                        AtananKaynakKodlari = kodlar
                    });
                    sNo++;
                }
            }

            var assignedIds = new List<Guid>();
            if (b.TryGetProperty("AtananKaynakIdleri", out var bkaynak) && bkaynak.ValueKind == JsonValueKind.Array)
            {
                foreach (var kid in bkaynak.EnumerateArray())
                {
                    var s = kid.ValueKind == JsonValueKind.String ? kid.GetString() : kid.ToString();
                    if (Guid.TryParse(s, out var gid) && gid != Guid.Empty)
                        assignedIds.Add(gid);
                }
            }

            list.Add(new ParsedEpisodeDraft
            {
                CalismaBasligi = ReadString(b, "CalismaBasligi"),
                Kanca = ReadString(b, "Kanca"),
                HedefSureDk = ReadInt(b, "HedefSureDk", fallbackDurationDk),
                AnaFikir = ReadString(b, "AnaFikir"),
                Konular = topics,
                SahneIskeleti = scenes,
                AtananKaynakIdleri = assignedIds.Distinct().ToList()
            });
        }

        return list;
    }

    private static string[] SafeDeserializeArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return LlmJson.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static object SafeDeserializeDynamic(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<object>();
        try
        {
            return LlmJson.Deserialize<JsonElement>(json);
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    private sealed class ParsedEpisodeDraft
    {
        public string CalismaBasligi { get; set; } = string.Empty;
        public string Kanca { get; set; } = string.Empty;
        public int HedefSureDk { get; set; }
        public string AnaFikir { get; set; } = string.Empty;
        public List<string> Konular { get; set; } = new();
        public List<SeriesSceneOutlineItemDto> SahneIskeleti { get; set; } = new();
        public List<Guid> AtananKaynakIdleri { get; set; } = new();
    }

    private static int ReadInt(JsonElement obj, string name, int fallback)
    {
        if (!obj.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.Number when v.TryGetDouble(out var d) => (int)Math.Round(d),
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => fallback
        };
    }

    private static bool ReadBool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
            _ => false
        };
    }

    private static string ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return string.Empty;
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.ToString();
    }
}
