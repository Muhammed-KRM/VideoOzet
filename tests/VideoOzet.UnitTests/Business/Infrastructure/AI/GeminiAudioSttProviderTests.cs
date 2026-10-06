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
using Xunit;

namespace VideoOzet.UnitTests.Business.Infrastructure.AI;

public class GeminiAudioSttProviderTests
{
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<GeminiAudioSttProvider>> _mockLogger;

    public GeminiAudioSttProviderTests()
    {
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<GeminiAudioSttProvider>>();

        _mockConfig.Setup(c => c["GEMINI_API_KEY"]).Returns("test-gemini-key");
    }

    [Fact]
    public async Task TranscribeAsync_ShouldReturnEmptyString_WhenApiKeyIsNotConfigured()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["GEMINI_API_KEY"]).Returns(string.Empty);

        var provider = new GeminiAudioSttProvider(new HttpClient(), config.Object, _mockLogger.Object);
        var stream = new MemoryStream(new byte[10]);

        var result = await provider.TranscribeAsync(stream, "test.mp3");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task TranscribeAsync_ShouldReturnTranscription_WhenApiCallIsSuccessful()
    {
        var expectedText = "Hukukun ontolojik temelleri dersine hoş geldiniz.";
        var jsonResponse = $@"{{
            ""choices"": [
                {{
                    ""message"": {{
                        ""role"": ""assistant"",
                        ""content"": ""{expectedText}""
                    }}
                }}
            ]
        }}";

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
        var provider = new GeminiAudioSttProvider(httpClient, _mockConfig.Object, _mockLogger.Object);
        var stream = new MemoryStream(new byte[10]);

        var result = await provider.TranscribeAsync(stream, "test.mp3");

        result.Should().Be(expectedText);
    }

    [Fact]
    public async Task TranscribeAsync_ShouldHandleChunkedAudio_WhenAudioExceedsDirectLimit()
    {
        var jsonResponse = @"{
            ""choices"": [
                {
                    ""message"": {
                        ""role"": ""assistant"",
                        ""content"": ""Bölüm parçası""
                    }
                }
            ]
        }";

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
        var provider = new GeminiAudioSttProvider(httpClient, _mockConfig.Object, _mockLogger.Object);
        var stream = new MemoryStream(new byte[600 * 1024]);

        var result = await provider.TranscribeAsync(stream, "large_audio.mp3");

        result.Should().NotBeNull();
    }
}
