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
    IConsumer<ContentErrorEvent>
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
}
