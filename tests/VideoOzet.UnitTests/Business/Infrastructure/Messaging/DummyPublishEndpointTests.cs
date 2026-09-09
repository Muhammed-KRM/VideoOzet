using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MassTransit;
using Moq;
using VideoOzet.Business.Infrastructure.Messaging;
using Xunit;

namespace VideoOzet.UnitTests.Business.Infrastructure.Messaging;

public class DummyPublishEndpointTests
{
    private readonly DummyPublishEndpoint _endpoint;

    public DummyPublishEndpointTests()
    {
        _endpoint = new DummyPublishEndpoint();
    }

    [Fact]
    public async Task Publish_GenericMessage_ShouldCompleteSuccessfully()
    {
        // Act
        var act = () => _endpoint.Publish(new { Text = "Test" }, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Publish_ObjectMessage_ShouldCompleteSuccessfully()
    {
        // Act
        var act = () => _endpoint.Publish((object)new { Text = "Test" }, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Publish_WithType_ShouldCompleteSuccessfully()
    {
        // Act
        var act = () => _endpoint.Publish(new { Text = "Test" }, typeof(object), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Publish_WithValues_ShouldCompleteSuccessfully()
    {
        // Act
        var act = () => _endpoint.Publish<object>(new { Text = "Test" }, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Publish_WithPipes_ShouldCompleteSuccessfully()
    {
        // Act & Assert
        await _endpoint.Publish("msg", (IPipe<PublishContext<string>>)null!, CancellationToken.None);
        await _endpoint.Publish("msg", (IPipe<PublishContext>)null!, CancellationToken.None);
        await _endpoint.Publish((object)"msg", (IPipe<PublishContext>)null!, CancellationToken.None);
        await _endpoint.Publish((object)"msg", typeof(string), (IPipe<PublishContext>)null!, CancellationToken.None);
        await _endpoint.Publish<object>((object)new { }, (IPipe<PublishContext<object>>)null!, CancellationToken.None);
        await _endpoint.Publish<object>((object)new { }, (IPipe<PublishContext>)null!, CancellationToken.None);
    }

    [Fact]
    public void ConnectPublishObserver_ShouldReturnConnectHandle()
    {
        // Arrange
        var mockObserver = new Mock<IPublishObserver>();

        // Act
        var handle = _endpoint.ConnectPublishObserver(mockObserver.Object);

        // Assert
        handle.Should().NotBeNull();
        var actDisconnect = () => handle.Disconnect();
        actDisconnect.Should().NotThrow();

        var actDispose = () => handle.Dispose();
        actDispose.Should().NotThrow();
    }
}
