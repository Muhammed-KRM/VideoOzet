using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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

public class SeriesVideoGenerationConsumer : IConsumer<SeriesVideoGenerationCommand>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ISourceTopicMapper _topicMapper;
    private readonly ISourceContextBuilder _sourceContextBuilder;
    private readonly IQualityCheckService _qcService;
    private readonly ILogService _logService;
    private readonly ILogger<SeriesVideoGenerationConsumer> _logger;

    public SeriesVideoGenerationConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        IEmbeddingProvider embeddingProvider,
        ISourceTopicMapper topicMapper,
        ILogService logService,
        ILogger<SeriesVideoGenerationConsumer> logger,
        ISourceContextBuilder? sourceContextBuilder = null,
        IQualityCheckService? qcService = null)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _embeddingProvider = embeddingProvider;
        _topicMapper = topicMapper;
        _sourceContextBuilder = sourceContextBuilder ?? new SourceContextBuilder(dbContext, embeddingProvider, Microsoft.Extensions.Logging.Abstractions.NullLogger<SourceContextBuilder>.Instance);
        _qcService = qcService ?? new QualityCheckService(synthesisProvider, Microsoft.Extensions.Logging.Abstractions.NullLogger<QualityCheckService>.Instance);
        _logService = logService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SeriesVideoGenerationCommand> context)
    {
        var message = context.Message;
        var logId = await _logService.LogPipelineStartAsync(null, message.EgitimId, PipelineAsamasi.Sentez, context.CorrelationId?.ToString());

        try
        {
            var request = await _dbContext.ContentRequests
                .Include(r => r.Egitim)
                .FirstOrDefaultAsync(r => r.Id == message.ContentRequestId, context.CancellationToken);

            if (request == null)
            {
                _logger.LogWarning("ContentRequest {Id} bulunamadı.", message.ContentRequestId);
                return;
            }

            var seriPlani = await _dbContext.SeriPlanlari
                .Include(p => p.SeriBolumler)
                .ThenInclude(b => b.Revizyonlar)
                .FirstOrDefaultAsync(p => p.Id == message.SeriPlaniId, context.CancellationToken);

            var bolum = seriPlani?.SeriBolumler.FirstOrDefault(b => b.Id == message.SeriBolumId);

            if (seriPlani == null || bolum == null)
            {
                throw new Exception("Seri planı veya bölüm bulunamadı.");
            }

            if (request.Durum == ContentRequestDurumu.Hata || seriPlani.Durum == SeriPlanDurumu.Iptal || bolum.Durum == BolumDurumu.Hata)
            {
                _logger.LogInformation("Seri {Id} veya Bölüm {BolumNo} durdurulduğu için üretim atlanıyor.", message.ContentRequestId, bolum.BolumNo);
                return;
            }

            bolum.Durum = BolumDurumu.Isleniyor;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = $"BolumUretimi-{bolum.BolumNo}",
                Durum = "Basladi",
                Mesaj = $"{bolum.BolumNo}. bölüm ({bolum.HedefSureDk} dk) için kaynaklar toplanıyor..."
            }, context.CancellationToken);

            // 1. Bölüm konularını, sahne iskeletini ve atanmış kaynak GUID listesini ayrıştır
            var topicsList = ParseTopicsList(bolum.KonularJson);
            if (!topicsList.Any() && !string.IsNullOrWhiteSpace(bolum.CalismaBasligi))
            {
                topicsList.Add(bolum.CalismaBasligi);
            }

            var sceneQueries = ParseSceneSearchQueries(bolum.SahneIskeletiJson);
            var assignedSourceIds = ParseAssignedSourceIds(bolum.AtananKaynaklarJson);

            var searchQueries = new List<string>();
            if (!string.IsNullOrWhiteSpace(bolum.CalismaBasligi)) searchQueries.Add(bolum.CalismaBasligi);
            if (!string.IsNullOrWhiteSpace(bolum.AnaFikir)) searchQueries.Add(bolum.AnaFikir);
            if (!string.IsNullOrWhiteSpace(bolum.Kanca)) searchQueries.Add(bolum.Kanca);
            if (!string.IsNullOrWhiteSpace(request.Konu)) searchQueries.Add(request.Konu);
            searchQueries.AddRange(topicsList);
            searchQueries.AddRange(sceneQueries);

            // 2. Bölüme atanan kaynakları tercih ederek geniş bütçeli (130.000 karakter) kaynak bağlamı çek
            IReadOnlyCollection<Guid>? preferredIds = assignedSourceIds.Count > 0 ? assignedSourceIds : null;
            var sourceContext = await _sourceContextBuilder.BuildContextAsync(
                message.EgitimId,
                searchQueries,
                maxChars: 130000,
                preferredSourceIds: preferredIds,
                targetChunksOverride: null,
                ct: context.CancellationToken);

            if (sourceContext == null)
            {
                sourceContext = await _sourceContextBuilder.BuildContextAsync(
                    message.EgitimId,
                    searchQueries,
                    maxChars: 130000,
                    ct: context.CancellationToken) ?? new SourceContextResult();
            }

            var allSourcesData = sourceContext.ContextText;
            var usedChunkIds = new List<Guid>(sourceContext.UsedChunkIds);

            if (!usedChunkIds.Any())
            {
                try
                {
                    var queryEmbedding = await _embeddingProvider.GenerateEmbeddingAsync($"{bolum.CalismaBasligi} {bolum.AnaFikir}");
                    var queryVector = new Pgvector.Vector(queryEmbedding);
                    var fallbackChunks = await GetRelevantChunksAsync(message.EgitimId, queryVector, 20, context.CancellationToken);
                    if (fallbackChunks != null && fallbackChunks.Any())
                    {
                        usedChunkIds.AddRange(fallbackChunks.Select(c => c.Id));
                        if (string.IsNullOrWhiteSpace(allSourcesData))
                        {
                            var sb = new StringBuilder();
                            int idx = 1;
                            foreach (var c in fallbackChunks)
                            {
                                sb.AppendLine($"=== [K{idx}] ===");
                                sb.AppendLine(c.Text);
                                idx++;
                            }
                            allSourcesData = sb.ToString();
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(allSourcesData))
            {
                allSourcesData = await _topicMapper.BuildTopicDigestAsync(message.EgitimId, context.CancellationToken);
            }

            // 3. Önceki bölümün gerçek DevirNotuJson (EpisodeContinuityLedgerDto) verisini bul
            string devirNotu = string.Empty;
            if (bolum.BolumNo > 1)
            {
                var oncekiBolum = seriPlani.SeriBolumler.FirstOrDefault(b => b.BolumNo == bolum.BolumNo - 1);
                if (oncekiBolum != null)
                {
                    var aktifRev = oncekiBolum.AktifRevizyonId.HasValue
                        ? oncekiBolum.Revizyonlar.FirstOrDefault(r => r.Id == oncekiBolum.AktifRevizyonId.Value)
                        : null;
                    aktifRev ??= oncekiBolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault();
                    devirNotu = aktifRev?.DevirNotuJson ?? string.Empty;
                }
            }

            // 3.5. Koruyucu Yeni Plan Versiyonu Kontrolü: Eğer PlanNo > 1 ise ve önceki onaylı planda bu bölümün son düzenlenmiş metni varsa baz al!
            string bazArastirmaOzeti = string.Empty;
            string bazVideoPlani = string.Empty;
            if (seriPlani.PlanNo > 1)
            {
                bool koruModu = true;
                if (!string.IsNullOrWhiteSpace(seriPlani.KullaniciKisitlariJson) &&
                    seriPlani.KullaniciKisitlariJson.Contains("\"MevcutMetinleriKoru\":false", StringComparison.OrdinalIgnoreCase))
                {
                    koruModu = false;
                }

                if (koruModu)
                {
                    var prevApprovedPlans = await _dbContext.SeriPlanlari
                        .AsNoTracking()
                        .Include(p => p.SeriBolumler)
                            .ThenInclude(b => b.Revizyonlar)
                        .Where(p => p.ContentRequestId == request.Id && p.PlanNo < seriPlani.PlanNo)
                        .OrderByDescending(p => p.PlanNo)
                        .ToListAsync(context.CancellationToken);

                    foreach (var prevPlan in prevApprovedPlans)
                    {
                        var prevEp = prevPlan.SeriBolumler.FirstOrDefault(b => b.BolumNo == bolum.BolumNo);
                        if (prevEp == null) continue;

                        var prevRev = prevEp.AktifRevizyonId.HasValue
                            ? prevEp.Revizyonlar.FirstOrDefault(r => r.Id == prevEp.AktifRevizyonId.Value)
                            : null;
                        prevRev ??= prevEp.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault();

                        if (prevRev != null && !string.IsNullOrWhiteSpace(prevRev.VideoPlani))
                        {
                            bazArastirmaOzeti = prevRev.ArastirmaOzeti ?? string.Empty;
                            bazVideoPlani = prevRev.VideoPlani;

                            // Yeni eklenen kaynakların kanıtlarını ek olarak çekip bağlamın en başına koy
                            var oldAssigned = ParseAssignedSourceIds(prevEp.AtananKaynaklarJson).ToHashSet();
                            var newlyAssigned = assignedSourceIds.Where(id => !oldAssigned.Contains(id)).ToList();
                            if (newlyAssigned.Count > 0)
                            {
                                try
                                {
                                    var newOnlyCtx = await _sourceContextBuilder.BuildContextAsync(
                                        message.EgitimId,
                                        searchQueries,
                                        maxChars: 28000,
                                        preferredSourceIds: newlyAssigned,
                                        targetChunksOverride: Math.Max(14, newlyAssigned.Count * 3),
                                        ct: context.CancellationToken);

                                    if (!string.IsNullOrWhiteSpace(newOnlyCtx?.ContextText))
                                    {
                                        var trimmedBaseSources = allSourcesData.Length > 34000
                                            ? allSourcesData.Substring(0, 34000)
                                            : allSourcesData;
                                        allSourcesData = $"=== BU YENİ PLAN VERSİYONUNDA EKLENEN YENİ KAYNAKLARDAN ÖNCELİKLİ KANITLAR ===\n{newOnlyCtx.ContextText}\n\n=== TÜM BÖLÜM KAYNAK KANITLARI ===\n{trimmedBaseSources}";
                                        usedChunkIds = newOnlyCtx.UsedChunkIds.Concat(usedChunkIds).Distinct().ToList();
                                    }
                                }
                                catch { }
                            }

                            _logger.LogInformation(
                                "Bölüm {BolumNo} (Plan v{PlanNo}): Plan v{PrevPlanNo} içindeki son düzenlenmiş metin ({Len} kr) koruyucu baz metin olarak alındı.",
                                bolum.BolumNo, seriPlani.PlanNo, prevPlan.PlanNo, bazVideoPlani.Length);
                            break;
                        }
                    }
                }
            }

            int hedefSureDk = bolum.HedefSureDk > 0
                ? bolum.HedefSureDk
                : (seriPlani.VarsayilanVideoSuresiDk > 0 ? seriPlani.VarsayilanVideoSuresiDk : 15);
            int toplamBolum = Math.Max(seriPlani.VideoSayisi, seriPlani.SeriBolumler.Count);
            string onayVePlanDirektifi = ExtractPlanApprovalAndRevisionDirective(seriPlani);

            var sonrakiBolum = seriPlani.SeriBolumler
                .OrderBy(b => b.BolumNo)
                .FirstOrDefault(b => b.BolumNo == bolum.BolumNo + 1);
            string sonrakiBolumOzeti = sonrakiBolum != null
                ? $"Bir sonraki video BÖLÜM {sonrakiBolum.BolumNo} ('{sonrakiBolum.CalismaBasligi}') olacaktır. Ana fikri: {sonrakiBolum.AnaFikir}. Konuları: {sonrakiBolum.KonularJson}. Kapanışta 'bir sonraki videoda...' geçişi yapacaksan YALNIZCA bu {sonrakiBolum.BolumNo}. bölümün konularına işaret et; daha ilerideki bölümlerin konularını bir sonraki videodaymış gibi söyleme!"
                : "BU BÖLÜM SERİNİN FİNAL BÖLÜMÜDÜR. Kapanışta 'bir sonraki videoda...' ifadesi KULLANMA; tüm seriyi bütünlüklü şekilde kapat.";

            var episodeRequest = new SeriesEpisodeGenerationRequest
            {
                BolumNo = bolum.BolumNo,
                ToplamBolum = toplamBolum,
                CalismaBasligi = bolum.CalismaBasligi,
                Kanca = bolum.Kanca ?? string.Empty,
                AnaFikir = bolum.AnaFikir ?? string.Empty,
                HedefSureDk = hedefSureDk,
                BolumKonulariJson = string.IsNullOrWhiteSpace(bolum.KonularJson) ? "[]" : bolum.KonularJson,
                SahneIskeletiJson = string.IsNullOrWhiteSpace(bolum.SahneIskeletiJson) ? "[]" : bolum.SahneIskeletiJson,
                YazarTeziVeGenelOdak = request.Konu ?? string.Empty,
                SeriKonusu = request.Konu ?? string.Empty,
                EkTonTalimati = request.EkTonTalimati ?? string.Empty,
                OnayTalimati = onayVePlanDirektifi,
                OncekiBolumDevirDefteriJson = devirNotu,
                SonrakiBolumOzeti = sonrakiBolumOzeti,
                BazAlinanMevcutArastirmaOzeti = bazArastirmaOzeti,
                BazAlinanMevcutVideoPlani = bazVideoPlani,
                HedefKitle = request.HedefKitle ?? "Genel",
                KaynakBaglamiText = allSourcesData
            };

            // 4. 3 Aşamalı AgentWrite Sahne-Sahne Üretim (SignalR canlı ilerleme yayınıyla)
            var (arastirmaOzeti, videoPlani, parsedDevirNotu) = await _synthesisProvider.GenerateSeriesEpisodeAsync(
                episodeRequest,
                onProgress: async (progressMsg, _) =>
                {
                    await context.Publish(new PipelineProgressEvent
                    {
                        VideoId = Guid.Empty,
                        EgitimId = message.EgitimId,
                        Asama = $"BolumUretimi-{bolum.BolumNo}",
                        Durum = "Isleniyor",
                        Mesaj = progressMsg
                    }, context.CancellationToken);
                },
                ct: context.CancellationToken);

            // Geriye uyumlu Moq fallback: Yalnızca eski GenerateSeriesVideoContentAsync metodunu mocklayan birim testler için
            if (string.IsNullOrWhiteSpace(arastirmaOzeti) && string.IsNullOrWhiteSpace(videoPlani))
            {
                var legacyJsonStr = await _synthesisProvider.GenerateSeriesVideoContentAsync(
                    bolum.CalismaBasligi,
                    bolum.KonularJson,
                    devirNotu,
                    request.HedefKitle ?? "Genel",
                    allSourcesData,
                    onayVePlanDirektifi,
                    context.CancellationToken);

                (arastirmaOzeti, videoPlani, parsedDevirNotu) = LlmJson.ParseSeriesVideoContent(legacyJsonStr);
            }

            if (string.IsNullOrWhiteSpace(parsedDevirNotu) || parsedDevirNotu == "{}")
            {
                parsedDevirNotu = LlmJson.NormalizeContinuityLedgerJson(
                    null,
                    bolum.BolumNo,
                    bolum.CalismaBasligi,
                    arastirmaOzeti,
                    videoPlani);
            }

            var yeniVersiyonNo = (bolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault()?.RevizyonNo ?? 0) + 1;
            var kullanilanKaynaklar = usedChunkIds.Any()
                ? JsonSerializer.Serialize(usedChunkIds)
                : (sourceContext.UsedSourceTitles.Any() ? JsonSerializer.Serialize(sourceContext.UsedSourceTitles) : "[]");

            var revizyon = new BolumRevizyonu
            {
                SeriBolumId = bolum.Id,
                RevizyonNo = yeniVersiyonNo,
                Tip = yeniVersiyonNo == 1 ? RevizyonTipi.IlkUretim : RevizyonTipi.KullaniciRevizyonu,
                Talimat = yeniVersiyonNo == 1 ? "Sistem tarafından ilk üretim" : "",
                ArastirmaOzeti = arastirmaOzeti,
                VideoPlani = videoPlani,
                DevirNotuJson = parsedDevirNotu,
                KullanilanKaynaklar = kullanilanKaynaklar,
                LlmModel = string.IsNullOrWhiteSpace(_synthesisProvider.ActiveModelName) ? "bilinmiyor" : _synthesisProvider.ActiveModelName,
                Durum = BolumDurumu.Tamamlandi
            };

            // ─── 5. VeriScore İki Katmanlı Kalite Kontrol (QC) ve Tez-Korumalı Auto-Refine ───
            _logger.LogInformation("Bölüm {BolumNo} için VeriScore Kalite Kontrol (QC) çalıştırılıyor...", bolum.BolumNo);
            try
            {
                var authorThesisContext = BuildAuthorThesisContextForQc(request, seriPlani, bolum, onayVePlanDirektifi);

                var qcResult = await _qcService.EvaluateAsync(
                    arastirmaOzeti,
                    allSourcesData,
                    authorThesisContext,
                    claimCount: 12,
                    previousReportJson: null,
                    videoPlanText: videoPlani,
                    ct: context.CancellationToken);

                if (qcResult == null)
                {
                    qcResult = await _qcService.EvaluateAsync(
                        arastirmaOzeti,
                        allSourcesData,
                        claimCount: 12,
                        previousReportJson: null,
                        ct: context.CancellationToken,
                        videoPlanText: videoPlani);
                }

                revizyon.GuvenSkorYuzde = qcResult.GuvenSkorYuzde;
                revizyon.ToplamIddiaSayisi = qcResult.ToplamIddiaSayisi;
                revizyon.DesteklenenSayisi = qcResult.DesteklenenSayisi;
                revizyon.BelirsizSayisi = qcResult.BelirsizSayisi;
                revizyon.DesteklenmeyenSayisi = qcResult.DesteklenmeyenSayisi;
                revizyon.DetayliRapor = qcResult.DetayliRaporJson;
                revizyon.QcDurumu = qcResult.Durum;

                var unsupportedFactualList = ExtractUnsupportedFactualClaims(qcResult.DetayliRaporJson);
                bool isAnchoredPreservationMode = !string.IsNullOrWhiteSpace(bazVideoPlani);
                if (!isAnchoredPreservationMode &&
                    qcResult.DesteklenmeyenSayisi > 0 &&
                    !string.IsNullOrWhiteSpace(allSourcesData) &&
                    !string.IsNullOrWhiteSpace(unsupportedFactualList))
                {
                    _dbContext.BolumRevizyonlari.Add(revizyon);

                    var refineInstruction =
                        "VERİSCORE TEZ VE ONAYLI PLAN KORUMALI ÖZ-DÜZELTME TALİMATI:\n" +
                        "1. [YAZAR-TEZİ] ve Onaylanmış Sahne Planı (`SahneIskeleti`) içinde açıkça adı geçen hiçbir düşünürü, müellifi, eseri (ör. Bediüzzaman Said Nursi, 26. Söz, İmam Mâtürîdî vb.), sahne başlığını, temsili veya paragraf derinliğini KESİNLİKLE SİLME veya KISALTMA!\n" +
                        "2. Yalnızca aşağıda listelenen ve ne kaynakta ne de onaylı planda karşılığı bulunmayan uydurma sayısal oranları (%) veya hayali deney/anketleri temizle ya da [K#] kanıtlarına uygun düzelt:\n" +
                        unsupportedFactualList;

                    var topicWithThesis = $"{bolum.CalismaBasligi}\n[YAZAR-TEZİ]: {authorThesisContext}";

                    var revisedSum = await _synthesisProvider.ReviseContentAsync(
                        arastirmaOzeti, refineInstruction, "Araştırma Özeti", topicWithThesis, allSourcesData, context.CancellationToken);
                    var revisedPlan = await _synthesisProvider.ReviseContentAsync(
                        videoPlani, refineInstruction, "Video Planı", topicWithThesis, allSourcesData, context.CancellationToken);

                    if (!string.IsNullOrWhiteSpace(revisedSum)) arastirmaOzeti = revisedSum;
                    if (!string.IsNullOrWhiteSpace(revisedPlan)) videoPlani = revisedPlan;

                    var reQc = await _qcService.EvaluateAsync(
                        arastirmaOzeti,
                        allSourcesData,
                        authorThesisContext,
                        claimCount: 12,
                        previousReportJson: qcResult.DetayliRaporJson,
                        videoPlanText: videoPlani,
                        ct: context.CancellationToken)
                        ?? await _qcService.EvaluateAsync(
                            arastirmaOzeti,
                            allSourcesData,
                            claimCount: 12,
                            previousReportJson: qcResult.DetayliRaporJson,
                            ct: context.CancellationToken,
                            videoPlanText: videoPlani);

                    revizyon = new BolumRevizyonu
                    {
                        SeriBolumId = bolum.Id,
                        RevizyonNo = yeniVersiyonNo + 1,
                        Tip = RevizyonTipi.AtifGuncelleme,
                        Talimat = "Otomatik Kanıt Öz-Düzeltmesi (Tez-Korumalı Auto-Refine)",
                        ArastirmaOzeti = arastirmaOzeti,
                        VideoPlani = videoPlani,
                        DevirNotuJson = parsedDevirNotu,
                        KullanilanKaynaklar = kullanilanKaynaklar,
                        LlmModel = string.IsNullOrWhiteSpace(_synthesisProvider.ActiveModelName) ? "bilinmiyor" : _synthesisProvider.ActiveModelName,
                        Durum = BolumDurumu.Tamamlandi,
                        GuvenSkorYuzde = reQc.GuvenSkorYuzde,
                        ToplamIddiaSayisi = reQc.ToplamIddiaSayisi,
                        DesteklenenSayisi = reQc.DesteklenenSayisi,
                        BelirsizSayisi = reQc.BelirsizSayisi,
                        DesteklenmeyenSayisi = reQc.DesteklenmeyenSayisi,
                        DetayliRapor = reQc.DetayliRaporJson,
                        QcDurumu = reQc.Durum
                    };
                }
            }
            catch (Exception qcEx)
            {
                _logger.LogWarning(qcEx, "Bölüm {BolumNo} QC değerlendirmesinde hata oluştu.", bolum.BolumNo);
                revizyon.GuvenSkorYuzde = 0m;
                revizyon.ToplamIddiaSayisi = 1;
                revizyon.DesteklenenSayisi = 0;
                revizyon.BelirsizSayisi = 1;
                revizyon.DesteklenmeyenSayisi = 0;
                revizyon.QcDurumu = BolumDurumu.Hata;
            }

            _dbContext.BolumRevizyonlari.Add(revizyon);
            bolum.AktifRevizyonId = revizyon.Id;
            bolum.BaglamEskidi = false;
            bolum.BaglamSorunlariJson = string.Empty;
            bolum.Durum = BolumDurumu.Tamamlandi;

            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = $"BolumUretimi-{bolum.BolumNo}",
                Durum = "Tamamlandi",
                Mesaj = $"{bolum.BolumNo}. bölüm ({hedefSureDk} dk) başarıyla üretildi."
            }, context.CancellationToken);

            await context.Publish(new SeriesVideoGeneratedEvent
            {
                ContentRequestId = request.Id,
                EgitimId = request.EgitimId,
                SeriBolumId = bolum.Id,
                BolumRevizyonuId = revizyon.Id
            }, context.CancellationToken);

            await _logService.LogPipelineEndAsync(logId, $"{{ \"status\": \"Success\", \"bolumNo\": {bolum.BolumNo} }}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeriesVideoGenerationConsumer hata fırlattı: {Message}", ex.Message);

            await ConsumerFailureGuard.TryPersistFailureStateAsync(_dbContext, _logger, async (db, ct) =>
            {
                var bolum = await db.SeriBolumler.FirstOrDefaultAsync(x => x.Id == message.SeriBolumId, ct);
                if (bolum != null)
                {
                    bolum.Durum = BolumDurumu.Hata;
                }
            });

            await ConsumerFailureGuard.TryRunAsync(_logger, "LogFunctionError",
                () => _logService.LogFunctionErrorAsync(nameof(SeriesVideoGenerationConsumer), ex, message));
            await ConsumerFailureGuard.TryRunAsync(_logger, "LogPipelineError",
                () => _logService.LogPipelineErrorAsync(logId, ex));

            await ConsumerFailureGuard.TryRunAsync(_logger, "Publish ContentErrorEvent",
                () => context.Publish(new ContentErrorEvent
                {
                    ContentRequestId = message.ContentRequestId,
                    EgitimId = message.EgitimId,
                    HataMesaji = $"Bölüm {message.BolumNo} üretim hatası: " + ex.Message
                }, CancellationToken.None));
        }
    }

    private static List<string> ParseTopicsList(string? konularJson)
    {
        var topicsList = new List<string>();
        if (string.IsNullOrWhiteSpace(konularJson))
            return topicsList;

        try
        {
            using var doc = JsonDocument.Parse(konularJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (elem.ValueKind == JsonValueKind.String)
                    {
                        var s = elem.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) topicsList.Add(s);
                    }
                    else if (elem.ValueKind == JsonValueKind.Object)
                    {
                        var b = elem.TryGetProperty("Baslik", out var bp) ? bp.GetString() : (elem.TryGetProperty("baslik", out bp) ? bp.GetString() : "");
                        var a = elem.TryGetProperty("Aciklama", out var ap) ? ap.GetString() : (elem.TryGetProperty("aciklama", out ap) ? ap.GetString() : "");
                        var combined = $"{b} {a}".Trim();
                        if (!string.IsNullOrWhiteSpace(combined)) topicsList.Add(combined);
                    }
                }
            }
        }
        catch
        {
            topicsList.Add(konularJson);
        }

        return topicsList;
    }

    private static List<string> ParseSceneSearchQueries(string? sahneIskeletiJson)
    {
        var queries = new List<string>();
        if (string.IsNullOrWhiteSpace(sahneIskeletiJson))
            return queries;

        try
        {
            using var doc = JsonDocument.Parse(sahneIskeletiJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var baslik = el.TryGetProperty("Baslik", out var bp) ? bp.GetString() : (el.TryGetProperty("baslik", out bp) ? bp.GetString() : "");
                    var arg = el.TryGetProperty("AciklamaVeArgumanlar", out var ap) ? ap.GetString() : (el.TryGetProperty("aciklamaVeArgumanlar", out ap) ? ap.GetString() : "");
                    var combined = $"{baslik} {arg}".Trim();
                    if (!string.IsNullOrWhiteSpace(combined))
                        queries.Add(combined);
                }
            }
        }
        catch { }

        return queries;
    }

    private static List<Guid> ParseAssignedSourceIds(string? atananKaynaklarJson)
    {
        var ids = new List<Guid>();
        if (string.IsNullOrWhiteSpace(atananKaynaklarJson))
            return ids;

        try
        {
            using var doc = JsonDocument.Parse(atananKaynaklarJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String && Guid.TryParse(el.GetString(), out var gid) && gid != Guid.Empty)
                    {
                        ids.Add(gid);
                    }
                    else if (el.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in el.EnumerateObject())
                        {
                            if ((string.Equals(prop.Name, "kaynakId", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(prop.Name, "id", StringComparison.OrdinalIgnoreCase)) &&
                                prop.Value.ValueKind == JsonValueKind.String &&
                                Guid.TryParse(prop.Value.GetString(), out var objGid) &&
                                objGid != Guid.Empty)
                            {
                                ids.Add(objGid);
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return ids.Distinct().ToList();
    }

    private static string ExtractPlanApprovalAndRevisionDirective(SeriPlani seriPlani)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(seriPlani.OnayTalimati))
        {
            parts.Add(seriPlani.OnayTalimati.Trim());
        }

        if (!string.IsNullOrWhiteSpace(seriPlani.KullaniciKisitlariJson) && seriPlani.KullaniciKisitlariJson.Trim() != "{}")
        {
            try
            {
                var json = LlmJson.ExtractJson(seriPlani.KullaniciKisitlariJson);
                if (!string.IsNullOrWhiteSpace(json) && json.StartsWith("{"))
                {
                    var dto = JsonSerializer.Deserialize<SeriesDraftDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (dto != null && !string.IsNullOrWhiteSpace(dto.RevizyonTalimati))
                    {
                        parts.Add(dto.RevizyonTalimati.Trim());
                    }
                }
            }
            catch { }
        }

        return string.Join(" | ", parts.Distinct());
    }

    private static string BuildAuthorThesisContextForQc(ContentRequest request, SeriPlani seriPlani, SeriBolum bolum, string onayDirektifi)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.Konu))
            sb.AppendLine($"Seri Odak ve Yazar Tezi: {request.Konu.Trim()}");
        if (!string.IsNullOrWhiteSpace(bolum.AnaFikir))
            sb.AppendLine($"Bölüm Ana Tezi: {bolum.AnaFikir.Trim()}");
        if (!string.IsNullOrWhiteSpace(bolum.KonularJson) && bolum.KonularJson != "[]")
            sb.AppendLine($"Onaylanmış Bölüm Konuları (Buradaki tüm konular ve düşünürler onaylıdır): {bolum.KonularJson}");
        if (!string.IsNullOrWhiteSpace(bolum.SahneIskeletiJson) && bolum.SahneIskeletiJson != "[]")
            sb.AppendLine($"Onaylanmış Sahne Planı Sözleşmesi (Burada geçen tüm düşünür, eser, temsil ve argümanlar ZORUNLUDUR ve ASLA desteklenmedi sayılamaz!): {bolum.SahneIskeletiJson}");
        if (!string.IsNullOrWhiteSpace(request.EkTonTalimati))
            sb.AppendLine($"Ek Ton ve Perspektif: {request.EkTonTalimati.Trim()}");
        if (!string.IsNullOrWhiteSpace(onayDirektifi))
            sb.AppendLine($"Plan Onay / Kurgu Direktifi: {onayDirektifi}");
        return sb.ToString().Trim();
    }

    private static string ExtractUnsupportedFactualClaims(string? reportJson)
    {
        if (string.IsNullOrWhiteSpace(reportJson))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(reportJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var durum = el.TryGetProperty("durum", out var d) ? d.GetString() : null;
                var iddiaTuru = el.TryGetProperty("iddiaTuru", out var it) ? it.GetString() : "olgusal_kaynak_iddiasi";

                if (string.Equals(durum, "desteklenmedi", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(iddiaTuru, "yazar_tezi_yorumu", StringComparison.OrdinalIgnoreCase))
                {
                    var iddia = el.TryGetProperty("iddia", out var i) ? i.GetString() : "";
                    var aciklama = el.TryGetProperty("aciklama", out var a) ? a.GetString() : "";
                    sb.AppendLine($"- {iddia} ({aciklama})");
                }
            }
            return sb.ToString().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    protected virtual async Task<List<VideoChunkDocument>> GetRelevantChunksAsync(
        Guid egitimId,
        Pgvector.Vector queryVector,
        int limit,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            var all = await _dbContext.VideoChunkDocuments
                .Where(c => c.EgitimId == egitimId)
                .ToListAsync(cancellationToken);
            return all.Take(limit).ToList();
        }

        return await _dbContext.VideoChunkDocuments
            .Where(c => c.EgitimId == egitimId)
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
