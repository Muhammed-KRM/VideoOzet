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

public class GroqWhisperSttProviderTests
{
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<GroqWhisperSttProvider>> _mockLogger;

    public GroqWhisperSttProviderTests()
    {
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<GroqWhisperSttProvider>>();
        _mockConfig.Setup(c => c["GROQ_API_KEY"]).Returns("gsk-test-key");
    }

    [Fact]
    public async Task TranscribeAsync_ShouldReturnEmptyString_WhenApiKeyMissing()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["GROQ_API_KEY"]).Returns(string.Empty);

        var provider = new GroqWhisperSttProvider(new HttpClient(), config.Object, _mockLogger.Object);
        var stream = new MemoryStream(new byte[100]);

        var result = await provider.TranscribeAsync(stream, "audio.mp3");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task TranscribeAsync_ShouldReturnTranscription_WhenGroqReturnsSuccess()
    {
        var expectedText = "Mukayeseli hukuk dersine hoş geldiniz.";
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

        var client = new HttpClient(mockHandler.Object);
        var provider = new GroqWhisperSttProvider(client, _mockConfig.Object, _mockLogger.Object);
        var stream = new MemoryStream(new byte[200]);

        var result = await provider.TranscribeAsync(stream, "audio.mp3");

        result.Should().Be(expectedText);
    }
}

public class CompositeSttProviderTests
{
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<CompositeSttProvider>> _mockLogger;

    public CompositeSttProviderTests()
    {
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<CompositeSttProvider>>();
    }

    [Fact]
    public async Task TranscribeAsync_ShouldCallGroqFirst_WhenGroqKeyIsPresent()
    {
        _mockConfig.Setup(c => c["GROQ_API_KEY"]).Returns("gsk-key");

        var groqHandler = new Mock<HttpMessageHandler>();
        groqHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"text\": \"Groq transcript\"}")
            });

        var groqProvider = new GroqWhisperSttProvider(new HttpClient(groqHandler.Object), _mockConfig.Object, Mock.Of<ILogger<GroqWhisperSttProvider>>());
        var geminiProvider = new GeminiAudioSttProvider(new HttpClient(), _mockConfig.Object, Mock.Of<ILogger<GeminiAudioSttProvider>>());

        var composite = new CompositeSttProvider(groqProvider, geminiProvider, _mockConfig.Object, _mockLogger.Object);
        var stream = new MemoryStream(new byte[50]);

        var result = await composite.TranscribeAsync(stream, "test.mp3");

        result.Should().Be("Groq transcript");
    }

    [Fact]
    public async Task TranscribeAsync_ShouldFallbackToGemini_WhenGroqFails()
    {
        _mockConfig.Setup(c => c["GROQ_API_KEY"]).Returns("gsk-key");
        _mockConfig.Setup(c => c["GEMINI_API_KEY"]).Returns("gemini-key");

        var groqHandler = new Mock<HttpMessageHandler>();
        groqHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("Server Error")
            });

        var geminiHandler = new Mock<HttpMessageHandler>();
        geminiHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(@"{""choices"": [{""message"": {""role"": ""assistant"", ""content"": ""Gemini fallback text""}}]}")
            });

        var groqProvider = new GroqWhisperSttProvider(new HttpClient(groqHandler.Object), _mockConfig.Object, Mock.Of<ILogger<GroqWhisperSttProvider>>());
        var geminiProvider = new GeminiAudioSttProvider(new HttpClient(geminiHandler.Object), _mockConfig.Object, Mock.Of<ILogger<GeminiAudioSttProvider>>());

        var composite = new CompositeSttProvider(groqProvider, geminiProvider, _mockConfig.Object, _mockLogger.Object);
        var stream = new MemoryStream(new byte[50]);

        var result = await composite.TranscribeAsync(stream, "test.mp3");

        result.Should().Be("Gemini fallback text");
    }
}
