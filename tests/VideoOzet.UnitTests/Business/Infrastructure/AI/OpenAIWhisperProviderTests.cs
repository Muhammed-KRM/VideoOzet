using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using VideoOzet.Business.Infrastructure.AI;

namespace VideoOzet.UnitTests.Business.Infrastructure.AI;

public class OpenAIWhisperProviderTests
{
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<OpenAIWhisperProvider>> _mockLogger;

    public OpenAIWhisperProviderTests()
    {
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<OpenAIWhisperProvider>>();

        _mockConfig.Setup(c => c["OpenAI:ApiKey"]).Returns("test-key");
        _mockConfig.Setup(c => c["OpenAI:WhisperModel"]).Returns("whisper-1");
    }

    [Fact]
    public async Task TranscribeAsync_ShouldReturnText_WhenApiCallIsSuccessful()
    {
        // Arrange
        var expectedText = "hello world";
        var jsonResponse = $"{{\"text\": \"{expectedText}\"}}";

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var provider = new OpenAIWhisperProvider(httpClient, _mockConfig.Object, _mockLogger.Object);
        var stream = new MemoryStream(new byte[10]);

        // Act
        var result = await provider.TranscribeAsync(stream, "test.mp3");

        // Assert
        result.Should().Be(expectedText);
    }

    [Fact]
    public async Task TranscribeAsync_ShouldThrowException_WhenApiCallFails()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("error")
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var provider = new OpenAIWhisperProvider(httpClient, _mockConfig.Object, _mockLogger.Object);
        var stream = new MemoryStream(new byte[10]);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => provider.TranscribeAsync(stream, "test.mp3"));
    }
}
