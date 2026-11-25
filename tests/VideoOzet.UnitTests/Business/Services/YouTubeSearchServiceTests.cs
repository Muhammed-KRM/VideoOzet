using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using Xunit;

namespace VideoOzet.UnitTests.Business.Services;

public class YouTubeSearchServiceTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<Uri> RequestedUris { get; } = new();

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != null)
            {
                RequestedUris.Add(request.RequestUri);
            }
            return Task.FromResult(_responder(request));
        }
    }

    private static YouTubeSearchService CreateService(
        string apiKey,
        StubHttpMessageHandler handler,
        Mock<ILogger<YouTubeSearchService>>? loggerMock = null)
    {
        var settingsMock = new Mock<IAiProviderSettingsService>();
        settingsMock.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiProviderSettingsDto { YouTubeApiKey = apiKey });

        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        return new YouTubeSearchService(
            httpFactoryMock.Object,
            settingsMock.Object,
            (loggerMock ?? new Mock<ILogger<YouTubeSearchService>>()).Object);
    }

    [Fact]
    public async Task SearchAsync_WithYouTubeApiKey_EnrichesSearchListWithVideosListMetadata()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/youtube/v3/search", StringComparison.OrdinalIgnoreCase))
            {
                const string searchJson = """
                {
                  "items": [
                    {
                      "id": { "videoId": "dQw4w9WgXcQ" },
                      "snippet": {
                        "title": "C# &amp; .NET Clean Architecture",
                        "channelTitle": "Code &amp; Craft",
                        "channelId": "UC12345",
                        "publishedAt": "2026-01-15T10:00:00Z",
                        "description": "Learn architecture",
                        "thumbnails": { "high": { "url": "https://img.example.com/1.jpg" } }
                      }
                    },
                    {
                      "id": { "videoId": "abcDEF12345" },
                      "snippet": {
                        "title": "Short Tip",
                        "channelTitle": "Dev Channel",
                        "channelId": "UC99999"
                      }
                    }
                  ]
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(searchJson, Encoding.UTF8, "application/json")
                };
            }

            if (url.Contains("/youtube/v3/videos", StringComparison.OrdinalIgnoreCase))
            {
                const string videosJson = """
                {
                  "items": [
                    {
                      "id": "dQw4w9WgXcQ",
                      "snippet": {
                        "title": "C# &amp; .NET Clean Architecture",
                        "channelTitle": "Code &amp; Craft",
                        "channelId": "UC12345",
                        "publishedAt": "2026-01-15T10:00:00Z",
                        "description": "Full detailed description",
                        "thumbnails": { "maxres": { "url": "https://img.example.com/1_max.jpg" } }
                      },
                      "contentDetails": { "duration": "PT1H02M05S" },
                      "statistics": { "viewCount": "123456" }
                    },
                    {
                      "id": "abcDEF12345",
                      "snippet": {
                        "title": "Short Tip",
                        "channelTitle": "Dev Channel",
                        "channelId": "UC99999"
                      },
                      "contentDetails": { "duration": "PT5M30S" },
                      "statistics": { "viewCount": "900" }
                    }
                  ]
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(videosJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService("AIzaTestKey123", handler);

        var result = await service.SearchAsync("clean architecture", maxResults: 10, duration: "medium");

        result.KullanilanKaynak.Should().Be("youtube-api-v3");
        result.KotaAsildiMi.Should().BeFalse();
        result.UyariMesaji.Should().BeNull();
        result.Items.Should().HaveCount(2);

        result.Items[0].VideoId.Should().Be("dQw4w9WgXcQ");
        result.Items[0].Baslik.Should().Be("C# & .NET Clean Architecture");
        result.Items[0].KanalAdi.Should().Be("Code & Craft");
        result.Items[0].SureSaniye.Should().Be(3725);
        result.Items[0].SureMetni.Should().Be("1:02:05");
        result.Items[0].GoruntulenmeSayisi.Should().Be(123456);
        result.Items[0].ThumbnailUrl.Should().Be("https://img.example.com/1_max.jpg");

        result.Items[1].VideoId.Should().Be("abcDEF12345");
        result.Items[1].SureSaniye.Should().Be(330);
        result.Items[1].SureMetni.Should().Be("05:30");

        handler.RequestedUris.Should().Contain(u => u.AbsoluteUri.Contains("videoDuration=medium"));
        handler.RequestedUris.Should().Contain(u => u.AbsoluteUri.Contains("/youtube/v3/videos"));
    }

    [Fact]
    public async Task SearchAsync_WhenDataApiReturns403QuotaExceeded_DetectsQuotaAndTriggersYtDlpFallback()
    {
        var loggerMock = new Mock<ILogger<YouTubeSearchService>>();
        var handler = new StubHttpMessageHandler(_ =>
        {
            const string quotaErrorJson = """
            {
              "error": {
                "code": 403,
                "message": "The request cannot be completed because you have exceeded your quota.",
                "errors": [
                  { "domain": "youtube.quota", "reason": "quotaExceeded" }
                ]
              }
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(quotaErrorJson, Encoding.UTF8, "application/json")
            };
        });

        var service = CreateService("AIzaQuotaLimitedKey", handler, loggerMock);

        try
        {
            var response = await service.SearchAsync("dotnet tutorial");
            // If yt-dlp happens to be installed on the machine, fallback succeeds with quota flag set
            response.KullanilanKaynak.Should().Be("yt-dlp");
            response.KotaAsildiMi.Should().BeTrue();
            response.UyariMesaji.Should().Contain("kotası aşıldı");
        }
        catch (BusinessException ex)
        {
            // When yt-dlp binary is absent in test environment, service still detected 403 quotaExceeded and attempted yt-dlp fallback
            ex.Message.Should().Contain("yt-dlp");
        }

        handler.RequestedUris.Should().ContainSingle(u => u.AbsoluteUri.Contains("/youtube/v3/search"));
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("KotaAşıldı=True") && v.ToString()!.Contains("yt-dlp")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData("dQw4w9WgXcQ", true, "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", true, "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s", true, "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?app=desktop&v=dQw4w9WgXcQ&t=10", true, "dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ", true, "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ", true, "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/abcDEF12345?t=10", true, "abcDEF12345")]
    [InlineData("https://www.youtube.com/shorts/xyz_987-AbC", true, "xyz_987-AbC")]
    [InlineData("https://www.youtube.com/embed/jNQXAC9IVRw", true, "jNQXAC9IVRw")]
    [InlineData("https://www.youtube-nocookie.com/embed/jNQXAC9IVRw?rel=0", true, "jNQXAC9IVRw")]
    [InlineData("https://youtube-nocookie.com/embed/jNQXAC9IVRw", true, "jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/live/12345678901", true, "12345678901")]
    [InlineData("", false, "")]
    [InlineData("too-short", false, "")]
    [InlineData("dQw4w9WgXcQ12345", false, "")]
    [InlineData("../../etc/passwd", false, "")]
    [InlineData("https://example.com/video.mp4", false, "")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ12345", false, "")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ12345", false, "")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ12345", false, "")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ12345", false, "")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ12345", false, "")]
    [InlineData("--exec=rm -rf / v=dQw4w9WgXcQ", false, "")]
    [InlineData("evil\"quote\"v=dQw4w9WgXcQ", false, "")]
    [InlineData("https://evil.com/watch?v=dQw4w9WgXcQ", false, "")]
    [InlineData("https://youtube.com.evil.com/watch?v=dQw4w9WgXcQ", false, "")]
    [InlineData("https://www.youtube.com/watch?otherv=dQw4w9WgXcQ", false, "")]
    [InlineData("javascript:alert(1)//youtube.com/watch?v=dQw4w9WgXcQ", false, "")]
    public void TryNormalizeVideoId_HandlesValidAndInvalidInputs(string input, bool expectedOk, string expectedId)
    {
        var ok = YouTubeSearchService.TryNormalizeVideoId(input, out var videoId);

        ok.Should().Be(expectedOk);
        videoId.Should().Be(expectedId);
    }

    [Theory]
    [InlineData("PT15S", 15)]
    [InlineData("PT5M30S", 330)]
    [InlineData("PT1H02M05S", 3725)]
    [InlineData("PT2H", 7200)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not-iso", null)]
    public void ParseIso8601DurationSeconds_ParsesExpectedDurations(string? iso, int? expectedSeconds)
    {
        YouTubeSearchService.ParseIso8601DurationSeconds(iso).Should().Be(expectedSeconds);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(0, "")]
    [InlineData(45, "00:45")]
    [InlineData(330, "05:30")]
    [InlineData(3725, "1:02:05")]
    public void FormatDuration_FormatsSecondsCleanly(int? seconds, string expected)
    {
        YouTubeSearchService.FormatDuration(seconds).Should().Be(expected);
    }
}
