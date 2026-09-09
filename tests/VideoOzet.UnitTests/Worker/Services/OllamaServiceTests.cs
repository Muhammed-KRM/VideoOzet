using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using VideoOzet.Worker.Services;

namespace VideoOzet.UnitTests.Worker.Services;

public class OllamaServiceTests
{
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<OllamaService>> _mockLogger;

    public OllamaServiceTests()
    {
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<OllamaService>>();

        // Setup a dummy URL so it fails to connect and hits the catch block
        _mockConfig.Setup(c => c["Ollama:BaseUrl"]).Returns("http://localhost:9999");
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnFalse_WhenOllamaIsUnreachable()
    {
        // Arrange
        var service = new OllamaService(_mockConfig.Object, _mockLogger.Object);

        // Act
        var result = await service.AnalyzeAsync("Test Title", "Test Description", CancellationToken.None);

        // Assert
        result.IsViolation.Should().BeFalse();
        result.ViolationType.Should().BeNull();
    }
}
