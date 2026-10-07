using MassTransit;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using VideoOzet.API.Hubs;
using VideoOzet.Business.Events;
using Microsoft.Extensions.Logging;

namespace VideoOzet.API.Consumers;

public class PipelineProgressConsumer : 
    IConsumer<PipelineProgressEvent>,
    IConsumer<ContentReadyEvent>,
    IConsumer<ContentProgressEvent>,
    IConsumer<ContentErrorEvent>,
    IConsumer<TopicAnalysisCompletedEvent>,
    IConsumer<SeriesPlanGeneratedEvent>,
    IConsumer<SeriesVideoGeneratedEvent>,
    IConsumer<SeriesPlanCompletedEvent>
{
    private readonly IHubContext<PipelineHub> _hubContext;
    private readonly ILogger<PipelineProgressConsumer> _logger;

    public PipelineProgressConsumer(IHubContext<PipelineHub> hubContext, ILogger<PipelineProgressConsumer> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PipelineProgressEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Pipeline Progress: {Asama} - {Durum} - {Mesaj}", evt.Asama, evt.Durum, evt.Mesaj);

        // Önyüze SignalR üzerinden olayı gönder
        await _hubContext.Clients.All.SendAsync("ReceiveProgress", evt);
    }

    public async Task Consume(ConsumeContext<ContentReadyEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("İçerik hazır: EgitimId={EgitimId}, ContentRequestId={RequestId}", evt.EgitimId, evt.ContentRequestId);

        await _hubContext.Clients.All.SendAsync("ContentReady", new 
        {
            contentRequestId = evt.ContentRequestId,
            egitimId = evt.EgitimId
        });
    }

    public async Task Consume(ConsumeContext<ContentProgressEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("İçerik Üretimi İlerleme: {Asama} - {Yuzde}%", evt.Asama, evt.Yuzde);

        await _hubContext.Clients.All.SendAsync("ContentProgress", evt);
    }

    public async Task Consume(ConsumeContext<ContentErrorEvent> context)
    {
        var evt = context.Message;
        _logger.LogError("İçerik Üretimi Hatası: EgitimId={EgitimId}, Hata={Hata}", evt.EgitimId, evt.HataMesaji);

        await _hubContext.Clients.All.SendAsync("ContentError", new 
        {
            contentRequestId = evt.ContentRequestId,
            egitimId = evt.EgitimId,
            hataMesaji = evt.HataMesaji
        });
    }

    public async Task Consume(ConsumeContext<TopicAnalysisCompletedEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Konu Analizi Tamamlandı: ContentRequestId={RequestId}", evt.ContentRequestId);

        await _hubContext.Clients.All.SendAsync("TopicAnalysisCompleted", evt);
        await _hubContext.Clients.All.SendAsync("ReceiveProgress", new
        {
            asama = "KonuAnalizi",
            durum = "Tamamlandi",
            mesaj = "Konu analizi tamamlandı.",
            contentRequestId = evt.ContentRequestId,
            egitimId = evt.EgitimId
        });
    }

    public async Task Consume(ConsumeContext<SeriesPlanGeneratedEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Seri Planı Hazır: ContentRequestId={RequestId}, SeriPlaniId={PlanId}", evt.ContentRequestId, evt.SeriPlaniId);

        await _hubContext.Clients.All.SendAsync("SeriesPlanGenerated", evt);
        await _hubContext.Clients.All.SendAsync("ReceiveProgress", new
        {
            asama = "SeriPlani",
            durum = "OnayBekliyor",
            mesaj = "Seri planı hazır, kullanıcı onayı bekleniyor.",
            contentRequestId = evt.ContentRequestId,
            egitimId = evt.EgitimId
        });
    }

    public async Task Consume(ConsumeContext<SeriesVideoGeneratedEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Seri Bölümü Hazır: BolumId={BolumId}", evt.SeriBolumId);

        await _hubContext.Clients.All.SendAsync("SeriesVideoGenerated", evt);
        await _hubContext.Clients.All.SendAsync("ReceiveProgress", new
        {
            asama = "BolumUretimi",
            durum = "Tamamlandi",
            mesaj = "Bölüm içeriği başarıyla oluşturuldu.",
            contentRequestId = evt.ContentRequestId,
            egitimId = evt.EgitimId,
            seriBolumId = evt.SeriBolumId
        });
    }

    public async Task Consume(ConsumeContext<SeriesPlanCompletedEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Seri Planı Tüm Bölümler Tamamlandı: ContentRequestId={RequestId}", evt.ContentRequestId);

        await _hubContext.Clients.All.SendAsync("SeriesPlanCompleted", evt);
        await _hubContext.Clients.All.SendAsync("ReceiveProgress", new
        {
            asama = "SeriPlani",
            durum = "Tamamlandi",
            mesaj = "Serideki tüm bölümler başarıyla tamamlandı!",
            contentRequestId = evt.ContentRequestId,
            egitimId = evt.EgitimId
        });
    }
}
