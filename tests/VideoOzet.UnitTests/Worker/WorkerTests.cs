using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VideoOzet.Worker;
using Xunit;

namespace VideoOzet.UnitTests.Worker;

public class WorkerTests
{
    [Fact]
    public async Task Worker_ShouldStartAndStopGracefully()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<VideoOzet.Worker.Worker>>();
        mockLogger.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(true);

        var worker = new VideoOzet.Worker.Worker(mockLogger.Object);
        using var cts = new CancellationTokenSource();

        // Act
        var startTask = worker.StartAsync(cts.Token);
        cts.Cancel(); // Immediately cancel stopping token
        await worker.StopAsync(CancellationToken.None);

        // Assert
        startTask.IsCompleted.Should().BeTrue();
    }
}
