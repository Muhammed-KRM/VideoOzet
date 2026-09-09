using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.API.Consumers;
using VideoOzet.API.Hubs;
using VideoOzet.Business.Events;
using Xunit;

namespace VideoOzet.UnitTests.API.Consumers;

public class PipelineProgressConsumerTests
{
    [Fact]
    public async Task Consume_ShouldSendSignalRMessage_WhenEventReceived()
    {
        // Arrange
        var hubContextMock = new Mock<IHubContext<PipelineHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);

        var loggerMock = new Mock<ILogger<PipelineProgressConsumer>>();

        var consumer = new PipelineProgressConsumer(hubContextMock.Object, loggerMock.Object);

        var contextMock = new Mock<ConsumeContext<PipelineProgressEvent>>();
        var evt = new PipelineProgressEvent
        {
            VideoId = Guid.NewGuid(),
            EgitimId = Guid.NewGuid(),
            Asama = "Test Asama",
            Durum = "Test Durum",
            Mesaj = "Test Mesaj"
        };
        contextMock.Setup(c => c.Message).Returns(evt);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        clientProxyMock.Verify(c => c.SendCoreAsync("ReceiveProgress", new object[] { evt }, It.IsAny<CancellationToken>()), Times.Once);
    }
}
