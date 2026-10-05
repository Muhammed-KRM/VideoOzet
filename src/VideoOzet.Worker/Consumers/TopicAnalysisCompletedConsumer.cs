using MassTransit;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VideoOzet.Business.Events;

namespace VideoOzet.Worker.Consumers;

public class TopicAnalysisCompletedConsumer : IConsumer<TopicAnalysisCompletedEvent>
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<TopicAnalysisCompletedConsumer> _logger;

    public TopicAnalysisCompletedConsumer(
        IPublishEndpoint publishEndpoint,
        ILogger<TopicAnalysisCompletedConsumer> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TopicAnalysisCompletedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Topic analysis completed for RequestId {RequestId}. Triggering SeriesPlanRequestedEvent.", msg.ContentRequestId);

        await _publishEndpoint.Publish(new SeriesPlanRequestedEvent
        {
            ContentRequestId = msg.ContentRequestId,
            EgitimId = msg.EgitimId
        }, context.CancellationToken);
    }
}
