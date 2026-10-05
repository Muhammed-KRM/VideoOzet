using MassTransit;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using Microsoft.Extensions.Logging;

namespace VideoOzet.Worker.Consumers;

public class SeriesVideoRevisionConsumer : IConsumer<SeriesVideoRevisionRequestedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SeriesVideoRevisionConsumer> _logger;

    public SeriesVideoRevisionConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        IPublishEndpoint publishEndpoint,
        ILogger<SeriesVideoRevisionConsumer> logger)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
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
                VideoId = bolum.Id,
                Asama = "Video revize ediliyor...",
                Durum = "İşleniyor",
                Mesaj = "Mevcut video içeriği güncelleniyor."
            });

            // Son revizyonu al
            var sonRevizyon = bolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault();
            int yeniRevizyonNo = (sonRevizyon?.RevizyonNo ?? 0) + 1;
            string currentOzet = sonRevizyon?.ArastirmaOzeti ?? "";
            string currentPlan = sonRevizyon?.VideoPlani ?? "";

            // Revizyon işlemini AI ile gerçekleştir (örnek olarak ikisini de revize ediyoruz)
            string yeniOzet = await _synthesisProvider.ReviseContentAsync(
                currentOzet,
                msg.Talimat,
                "Araştırma Özeti",
                bolum.CalismaBasligi,
                "",
                context.CancellationToken);

            string yeniPlan = await _synthesisProvider.ReviseContentAsync(
                currentPlan,
                msg.Talimat,
                "Video Planı",
                bolum.CalismaBasligi,
                "",
                context.CancellationToken);

            var yeniRevizyon = new BolumRevizyonu
            {
                SeriBolumId = bolum.Id,
                RevizyonNo = yeniRevizyonNo,
                Talimat = msg.Talimat,
                ArastirmaOzeti = yeniOzet,
                VideoPlani = yeniPlan,
                DevirNotuJson = sonRevizyon?.DevirNotuJson ?? "",
                LlmModel = _synthesisProvider.ActiveModelName,
                Tip = RevizyonTipi.KullaniciRevizyonu,
                Durum = BolumDurumu.Tamamlandi
            };

            _dbContext.BolumRevizyonlari.Add(yeniRevizyon);
            
            bolum.AktifRevizyonId = yeniRevizyon.Id;
            bolum.Durum = BolumDurumu.Tamamlandi;

            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = bolum.Id,
                Asama = "Revizyon Tamamlandı",
                Durum = "Başarılı",
                Mesaj = "Video başarıyla güncellendi."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seri video revizyonunda hata. BolumId: {BolumId}", msg.SeriBolumId);
            
            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = msg.SeriBolumId,
                Asama = "Hata",
                Durum = "Hata",
                Mesaj = $"Revizyon hatası: {ex.Message}"
            });
            
            var bolum = await _dbContext.SeriBolumler.FirstOrDefaultAsync(b => b.Id == msg.SeriBolumId);
            if (bolum != null)
            {
                bolum.Durum = BolumDurumu.Tamamlandi; // Eski haline döndür
                await _dbContext.SaveChangesAsync();
            }
        }
    }
}
