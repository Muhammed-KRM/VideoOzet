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
using VideoOzet.Business.Infrastructure.Media;
using Xunit;

namespace VideoOzet.UnitTests.Business.Infrastructure;

public class YtDlpMediaServiceTests
{
    private static YtDlpMediaService CreateService(HttpMessageHandler? handler = null)
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var client = handler != null ? new HttpClient(handler) : new HttpClient();
        httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return new YtDlpMediaService(httpClientFactory.Object, new Mock<ILogger<YtDlpMediaService>>().Object);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    [Fact]
    public void ThrowIfHlsManifestHasDrm_WithAes128AndSkdUri_ShouldThrowDrmProtectedContentException()
    {
        const string manifest = "#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"skd://asset1\"";

        Action act = () => YtDlpMediaService.ThrowIfHlsManifestHasDrm(manifest);

        act.Should().Throw<DrmProtectedContentException>();
    }

    [Theory]
    [InlineData("ERROR: Unable to download: edef8ba9-79d6-4ace-a3c8-27dcd51d21ed")]
    [InlineData("Key URI skd://fairplay-key-server detected")]
    public void ThrowIfDrmDetected_WithWidevineUuidOrSkdUri_ShouldThrowDrmProtectedContentException(string stderr)
    {
        Action act = () => YtDlpMediaService.ThrowIfDrmDetected(stderr);

        act.Should().Throw<DrmProtectedContentException>();
    }

    [Fact]
    public void ParseVtt_WithIntraWordTagsAndTwoLineRepeatedCues_ShouldKeepWordsIntactAndDeduplicate()
    {
        const string vtt = """
            WEBVTT

            00:00:01.000 --> 00:00:03.000
            birinci satır
            ikinci satır

            00:00:03.000 --> 00:00:05.000
            birinci satır
            ikinci satır

            00:00:05.000 --> 00:00:08.000
            mer<00:00:07.500><c>haba</c> test
            """;

        var (hamMetin, _) = YtDlpMediaService.ParseVtt(vtt);

        hamMetin.Should().Be("birinci satır ikinci satır merhaba test");
    }

    [Theory]
    [InlineData("{bad json")]
    [InlineData("")]
    public void ParseCourseManifestJson_WhenMalformedOrEmpty_ShouldThrowBusinessException(string json)
    {
        Action act = () => YtDlpMediaService.ParseCourseManifestJson(json, "https://example.com/manifest.json");

        act.Should().Throw<BusinessException>()
            .WithMessage("*JSON ders manifesti*");
    }

    [Fact]
    public async Task ExpandPlaylistOrManifestAsync_WhenHttpReturnsMalformedJson_ShouldThrowBusinessExceptionNotJsonReaderException()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{bad json", Encoding.UTF8, "application/json")
        });

        var svc = CreateService(handler);

        Func<Task> act = () => svc.ExpandPlaylistOrManifestAsync("https://example.com/bad.json", null);

        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*JSON ders manifesti*");
    }

    [Fact]
    public void ParseJson3_WhenMalformedJson_ShouldReturnEmptyWithoutThrowing()
    {
        var (hamMetin, segmentsJson) = YtDlpMediaService.ParseJson3("{broken json3");

        hamMetin.Should().BeEmpty();
        segmentsJson.Should().Be("[]");
    }

    [Fact]
    public void ParseSingleMetadataJson_ShouldFilterOutAutoTranslateTlangEntries()
    {
        const string metadataJson = """
        {
          "id": "dQw4w9WgXcQ",
          "title": "Sample Video",
          "webpage_url": "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
          "subtitles": {
            "tr": [ { "url": "https://www.youtube.com/api/timedtext?v=dQw4w9WgXcQ&lang=tr" } ]
          },
          "automatic_captions": {
            "af": [ { "url": "https://www.youtube.com/api/timedtext?v=dQw4w9WgXcQ&lang=en&tlang=af" } ],
            "en": [ { "url": "https://www.youtube.com/api/timedtext?v=dQw4w9WgXcQ&lang=en" } ],
            "live_chat": [ { "url": "https://www.youtube.com/api/live_chat" } ]
          }
        }
        """;

        var meta = YtDlpMediaService.ParseSingleMetadataJson(metadataJson, "https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        meta.MevcutAltyaziDilleri.Should().Contain(new[] { "tr", "en" });
        meta.MevcutAltyaziDilleri.Should().NotContain("af");
        meta.MevcutAltyaziDilleri.Should().NotContain("live_chat");
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/video.mp4")]
    [InlineData("javascript:alert(1)")]
    [InlineData("--exec=calc.exe")]
    [InlineData("https://example.com/\\\" --exec \\\"calc.exe")]
    public async Task UnsafeUrlOrFlagInjection_ShouldBeRejectedWithBusinessException(string unsafeUrl)
    {
        Action syncAct = () => YtDlpMediaService.ValidateHttpUrl(unsafeUrl);
        syncAct.Should().Throw<BusinessException>();

        var svc = CreateService();
        Func<Task> metadataAct = () => svc.GetMetadataAsync(unsafeUrl);
        await metadataAct.Should().ThrowAsync<BusinessException>();

        Func<Task> expandAct = () => svc.ExpandPlaylistOrManifestAsync(unsafeUrl, null);
        await expandAct.Should().ThrowAsync<BusinessException>();

        Func<Task> downloadAct = () => svc.DownloadAudioAsync(unsafeUrl, System.IO.Path.GetTempPath(), null, null);
        await downloadAct.Should().ThrowAsync<BusinessException>();
    }

    [Theory]
    [InlineData("valid-token\r\nX-Injected: 1")]
    [InlineData("valid-token\nEvil: true")]
    public async Task BearerTokenWithCrLf_ShouldBeRejectedWithBusinessException(string unsafeToken)
    {
        Action syncAct = () => YtDlpMediaService.ValidateAndNormalizeBearerToken(unsafeToken);
        syncAct.Should().Throw<BusinessException>();

        var svc = CreateService();
        Func<Task> downloadAct = () => svc.DownloadAudioAsync("https://example.com/video.mp4", System.IO.Path.GetTempPath(), unsafeToken, null);
        await downloadAct.Should().ThrowAsync<BusinessException>();

        Func<Task> expandAct = () => svc.ExpandPlaylistOrManifestAsync("https://example.com/course.json", unsafeToken);
        await expandAct.Should().ThrowAsync<BusinessException>();
    }

    [Theory]
    [InlineData("https://youtube.com.attacker.com/a.mp4")]
    [InlineData("https://youtube.com.attacker.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://notyoutu.be/a.mp4")]
    [InlineData("https://notyoutu.be/dQw4w9WgXcQ")]
    [InlineData("https://user:pass@youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("ftp://youtube.com/watch?v=dQw4w9WgXcQ")]
    public void SpoofedOrNonHttpYouTubeUrls_ShouldReturnFalseAndNullVideoId(string url)
    {
        YtDlpMediaService.IsYouTubeUrl(url).Should().BeFalse();
        YtDlpMediaService.ExtractYouTubeVideoId(url).Should().BeNull();
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?list=PL123&v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=42", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube-nocookie.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void AllowedYouTubeUrls_ShouldBeRecognizedAndExtractExactVideoId(string url, string expectedId)
    {
        YtDlpMediaService.IsYouTubeUrl(url).Should().BeTrue();
        YtDlpMediaService.ExtractYouTubeVideoId(url).Should().Be(expectedId);
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PL123")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ12345")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ/extra")]
    public void AllowedYouTubeUrlsWithoutSingleValidVideoId_ShouldReturnTrueForHostAndNullForId(string url)
    {
        YtDlpMediaService.IsYouTubeUrl(url).Should().BeTrue();
        YtDlpMediaService.ExtractYouTubeVideoId(url).Should().BeNull();
    }

    [Theory]
    [InlineData("""{"events":[123]}""")]
    [InlineData("""{"events":[{"segs":[123]}]}""")]
    [InlineData("""{"events":[{"tStartMs":"bad","dDurationMs":null,"segs":[{"utf8":123}]}]}""")]
    [InlineData("[1,2,3]")]
    public void ParseJson3_WithNonObjectElementsOrUnexpectedTypes_ShouldReturnEmptyWithoutThrowing(string json3)
    {
        var (hamMetin, segmentsJson) = YtDlpMediaService.ParseJson3(json3);

        hamMetin.Should().BeEmpty();
        segmentsJson.Should().Be("[]");
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("""["abc"]""")]
    [InlineData("""{"events":[123]}""")]
    [InlineData("""{"events":[{"segs":[123]}]}""")]
    public async Task TryFetchTranscriptFromUrlAsync_WithNonObjectJsonArrayElements_ShouldReturnNullWithoutThrowing(string body)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        var svc = CreateService(handler);

        var result = await svc.TryFetchTranscriptFromUrlAsync("https://example.com/transcript.json", null, "tr");

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryFetchTranscriptFromUrlAsync_WithMixedSegmentArray_ShouldSkipNonObjectsAndKeepValidSegments()
    {
        const string body = """[123, "bad", {"text": 99}, {"text": "Merhaba dünya", "start": "bad"}]""";
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        var svc = CreateService(handler);

        var result = await svc.TryFetchTranscriptFromUrlAsync("https://example.com/transcript.json", null, "tr");

        result.Should().NotBeNull();
        result!.HamMetin.Should().Be("Merhaba dünya");
    }

    [Theory]
    [InlineData("tr-orig", "tr")]
    [InlineData("tr-TR", "tr")]
    [InlineData("en-US-orig", "en")]
    [InlineData("EN", "en")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeLanguageCode_ShouldExtractIso2BaseCode(string? raw, string expected)
    {
        YtDlpMediaService.NormalizeLanguageCode(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("tr", "tr", "en", true)]
    [InlineData("tr-TR", "tr", "en-US", true)]
    [InlineData("en", "tr", "en", false)]
    [InlineData("tr", "tr", "tr-orig", false)]
    [InlineData("tr", "tr", "tr", false)]
    [InlineData(null, "tr", "en", false)]
    public void ShouldRejectAutoTranslatedCaption_ShouldRejectMismatchedTranslationWhenOriginalMatchesPreference(
        string? videoOriginalLang,
        string? preferredLang,
        string? selectedLang,
        bool expectedReject)
    {
        YtDlpMediaService.ShouldRejectAutoTranslatedCaption(videoOriginalLang, preferredLang, selectedLang)
            .Should().Be(expectedReject);
    }
}
