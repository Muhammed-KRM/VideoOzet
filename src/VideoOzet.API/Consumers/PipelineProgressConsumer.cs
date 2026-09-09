using MassTransit;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using VideoOzet.API.Hubs;
using VideoOzet.Business.Events;
using Microsoft.Extensions.Logging;

namespace VideoOzet.API.Consumers;

public class PipelineProgressConsumer : IConsumer<PipelineProgressEvent>
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
}
