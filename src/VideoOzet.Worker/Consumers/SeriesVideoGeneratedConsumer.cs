using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Data.Context;
using VideoOzet.Data.Enums;

namespace VideoOzet.Worker.Consumers;

public class SeriesVideoGeneratedConsumer : IConsumer<SeriesVideoGeneratedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SeriesVideoGeneratedConsumer> _logger;

    public SeriesVideoGeneratedConsumer(
        AppDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        ILogger<SeriesVideoGeneratedConsumer> logger)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SeriesVideoGeneratedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("SeriesVideoGeneratedEvent received for BolumId {BolumId}", msg.SeriBolumId);

        var bolum = await _dbContext.SeriBolumler
            .Include(b => b.SeriPlani)
                .ThenInclude(p => p.SeriBolumler)
                    .ThenInclude(sb => sb.Revizyonlar)
            .Include(b => b.SeriPlani)
                .ThenInclude(p => p.ContentRequest)
            .FirstOrDefaultAsync(b => b.Id == msg.SeriBolumId, context.CancellationToken);

        if (bolum == null || bolum.SeriPlani == null)
        {
            _logger.LogWarning("SeriBolum veya SeriPlani bulunamadı. ID: {BolumId}", msg.SeriBolumId);
            return;
        }

        var plan = bolum.SeriPlani;
        var nextBolum = plan.SeriBolumler
            .OrderBy(b => b.BolumNo)
            .FirstOrDefault(b => b.BolumNo == bolum.BolumNo + 1);

        if (nextBolum != null)
        {
            // İdempotent kontrol: Eğer sonraki bölüm zaten bir revizyona sahipse veya işleniyorsa tekrar tetikleme
            if (nextBolum.Revizyonlar.Any() || nextBolum.Durum == BolumDurumu.Isleniyor)
            {
                _logger.LogInformation("Sonraki bölüm {BolumNo} zaten başlatılmış veya üretilmiş.", nextBolum.BolumNo);
                return;
            }

            _logger.LogInformation("Sıradaki video {BolumNo} üretimi tetikleniyor...", nextBolum.BolumNo);
            nextBolum.Durum = BolumDurumu.Isleniyor;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await _publishEndpoint.Publish(new SeriesVideoGenerationCommand
            {
                ContentRequestId = msg.ContentRequestId,
                EgitimId = msg.EgitimId,
                SeriPlaniId = plan.Id,
                SeriBolumId = nextBolum.Id,
                BolumNo = nextBolum.BolumNo
            }, context.CancellationToken);
        }
        else
        {
            // Tüm bölümler tamamlandı!
            _logger.LogInformation("Tüm bölümler tamamlandı. Plan ID: {PlanId}", plan.Id);

            if (plan.ContentRequest != null)
            {
                plan.ContentRequest.Durum = ContentRequestDurumu.Tamamlandi;
                plan.ContentRequest.TamamlanmaTarihi = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = msg.EgitimId,
                Asama = "SeriUretimi",
                Durum = "Tamamlandi",
                Mesaj = $"Tüm seri ({plan.SeriBolumler.Count} video) başarıyla üretildi."
            }, context.CancellationToken);

            await _publishEndpoint.Publish(new SeriesPlanCompletedEvent
            {
                ContentRequestId = msg.ContentRequestId,
                EgitimId = msg.EgitimId,
                SeriPlaniId = plan.Id
            }, context.CancellationToken);
        }
    }
}
