using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.EntityFrameworkCore;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Events;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Consumers;
using Xunit;

namespace VideoOzet.UnitTests.Worker;

public class DownloadRemoteVideoConsumerTests
{
    private static (
        DownloadRemoteVideoConsumer Consumer,
        Mock<AppDbContext> DbContextMock,
        Mock<DbSet<Video>> VideoDbSetMock,
        Mock<DbSet<VideoTranscript>> TranscriptDbSetMock,
        List<VideoTranscript> SavedTranscripts,
        Mock<ICacheService> CacheMock,
        Mock<IRemoteMediaService> RemoteMediaMock,
        Mock<ITranscriptQualityEvaluator> QualityEvaluatorMock,
        Mock<IFileStorageService> FileStorageMock,
        Mock<IPublishEndpoint> PublishMock,
        Mock<IPipelineStateTracker> StateTrackerMock) CreateSut(Video video)
    {
        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var videoDbSetMock = new Mock<DbSet<Video>>();
        var transcriptDbSetMock = new Mock<DbSet<VideoTranscript>>();
        var savedTranscripts = new List<VideoTranscript>();

        dbContextMock.Setup(x => x.Videolar).ReturnsDbSet(new List<Video> { video }, videoDbSetMock);
        videoDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object[] keyValues, CancellationToken _) =>
                (Guid)keyValues[0] == video.Id ? video : null);

        dbContextMock.Setup(x => x.VideoTranscripts).ReturnsDbSet(savedTranscripts, transcriptDbSetMock);
        transcriptDbSetMock.Setup(x => x.Add(It.IsAny<VideoTranscript>()))
            .Callback<VideoTranscript>(t => savedTranscripts.Add(t));

        var cacheMock = new Mock<ICacheService>();
        cacheMock.Setup(c => c.SetIfNotExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        var remoteMediaMock = new Mock<IRemoteMediaService>();
        var qualityEvaluatorMock = new Mock<ITranscriptQualityEvaluator>();
        var fileStorageMock = new Mock<IFileStorageService>();
        var publishMock = new Mock<IPublishEndpoint>();
        var stateTrackerMock = new Mock<IPipelineStateTracker>();
        var heartbeatMock = new Mock<IAsyncDisposable>();
        stateTrackerMock
            .Setup(s => s.StartHeartbeat(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(heartbeatMock.Object);

        var consumer = new DownloadRemoteVideoConsumer(
            new Mock<ILogger<DownloadRemoteVideoConsumer>>().Object,
            cacheMock.Object,
            remoteMediaMock.Object,
            qualityEvaluatorMock.Object,
            fileStorageMock.Object,
            dbContextMock.Object,
            publishMock.Object,
            stateTrackerMock.Object);

        return (consumer, dbContextMock, videoDbSetMock, transcriptDbSetMock, savedTranscripts,
            cacheMock, remoteMediaMock, qualityEvaluatorMock, fileStorageMock, publishMock, stateTrackerMock);
    }

    private static ConsumeContext<RemoteVideoRequestedEvent> CreateContext(RemoteVideoRequestedEvent message)
    {
        var ctx = new Mock<ConsumeContext<RemoteVideoRequestedEvent>>();
        ctx.Setup(c => c.Message).Returns(message);
        ctx.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    [Theory]
    [InlineData(true, "youtube-captions-manual", 95)]
    [InlineData(false, "youtube-captions-auto", 82)]
    public async Task Consume_WhenCaptionsPassQualityGate_ShouldSaveTranscriptPublishTranscriptReadyAndSkipSttDownload(
        bool isManual,
        string expectedSttModel,
        int qualityScore)
    {
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var video = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "YouTube Video",
            KaynakPlatform = KaynakPlatform.YouTube,
            KaynakUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            IslemDurumu = VideoIslemDurumu.Indiriliyor
        };

        var sut = CreateSut(video);

        var captionText = string.Join(' ', Enumerable.Repeat("kaliteli eğitim transkript kelimesi", 20));
        sut.RemoteMediaMock
            .Setup(m => m.TryFetchCaptionsAsync(video.KaynakUrl!, "tr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteCaptionResult
            {
                HamMetin = captionText,
                ZamanDamgalariJson = "[{\"start\":0,\"end\":5,\"text\":\"kaliteli eğitim\"}]",
                Dil = "tr",
                IsManual = isManual,
                SureSaniye = 120
            });

        sut.QualityEvaluatorMock
            .Setup(q => q.Evaluate(captionText, 120, "tr", isManual))
            .Returns(new TranscriptQualityReport(qualityScore, true, "Kalite kapısından geçti", 60.0, 0.05));

        var msg = new RemoteVideoRequestedEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            KaynakUrl = video.KaynakUrl!,
            KaynakPlatform = KaynakPlatform.YouTube,
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            TercihEdilenDil = "tr"
        };

        await sut.Consumer.Consume(CreateContext(msg));

        video.IslemDurumu.Should().Be(VideoIslemDurumu.SttTamamlandi);
        video.TranskriptKaliteSkoru.Should().Be(qualityScore);
        video.Sure.Should().Be(TimeSpan.FromSeconds(120));

        sut.SavedTranscripts.Should().ContainSingle();
        var saved = sut.SavedTranscripts[0];
        saved.VideoId.Should().Be(videoId);
        saved.SttModel.Should().Be(expectedSttModel);
        saved.HamMetin.Should().Be(captionText);
        saved.Dil.Should().Be("tr");

        sut.PublishMock.Verify(
            p => p.Publish(It.Is<TranscriptReadyEvent>(e => e.VideoId == videoId && e.EgitimId == egitimId), It.IsAny<CancellationToken>()),
            Times.Once);

        sut.RemoteMediaMock.Verify(
            m => m.DownloadAudioAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Action<int, string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        sut.FileStorageMock.Verify(
            f => f.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        sut.PublishMock.Verify(
            p => p.Publish(It.IsAny<VideoUploadedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        sut.CacheMock.Verify(c => c.RemoveAsync($"lock:download:{videoId}"), Times.Once);
    }

    [Fact]
    public async Task Consume_WhenCaptionFailsQualityGate_ShouldFallbackToAudioDownloadAndPublishVideoUploaded()
    {
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var video = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "Noisy Captions Video",
            KaynakPlatform = KaynakPlatform.YouTube,
            KaynakUrl = "https://www.youtube.com/watch?v=abcDEF12345",
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            IslemDurumu = VideoIslemDurumu.Indiriliyor
        };

        var sut = CreateSut(video);

        sut.RemoteMediaMock
            .Setup(m => m.TryFetchCaptionsAsync(video.KaynakUrl!, "tr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteCaptionResult
            {
                HamMetin = "[Müzik] [Müzik] [Alkış] kısa",
                ZamanDamgalariJson = "[]",
                Dil = "tr",
                IsManual = false,
                SureSaniye = 90
            });

        sut.QualityEvaluatorMock
            .Setup(q => q.Evaluate(It.IsAny<string>(), 90, "tr", false))
            .Returns(new TranscriptQualityReport(20, false, "Yetersiz kelime ve gürültü", 5.0, 0.0));

        sut.RemoteMediaMock
            .Setup(m => m.DownloadAudioAsync(video.KaynakUrl!, It.IsAny<string>(), null, It.IsAny<Action<int, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string dir, string? _, Action<int, string>? _, CancellationToken _) =>
            {
                Directory.CreateDirectory(dir);
                var audioPath = Path.Combine(dir, "downloaded.m4a");
                File.WriteAllBytes(audioPath, new byte[] { 1, 2, 3, 4, 5, 6 });
                return audioPath;
            });

        bool wasSeekableStream = false;
        long uploadedStreamLength = 0;
        sut.FileStorageMock
            .Setup(f => f.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), "audio/mp4"))
            .Callback<Stream, string, string>((stream, _, _) =>
            {
                wasSeekableStream = stream.CanSeek;
                uploadedStreamLength = stream.Length;
            })
            .ReturnsAsync("remote/uploaded-audio.m4a");

        var msg = new RemoteVideoRequestedEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            KaynakUrl = video.KaynakUrl!,
            KaynakPlatform = KaynakPlatform.YouTube,
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            TercihEdilenDil = "tr"
        };

        await sut.Consumer.Consume(CreateContext(msg));

        wasSeekableStream.Should().BeTrue();
        uploadedStreamLength.Should().Be(6);
        video.TranskriptKaliteSkoru.Should().Be(20);
        video.DosyaYolu.Should().Be("remote/uploaded-audio.m4a");
        video.DosyaBoyutu.Should().Be(6);
        video.IslemDurumu.Should().Be(VideoIslemDurumu.Bekliyor);

        sut.PublishMock.Verify(
            p => p.Publish(It.Is<VideoUploadedEvent>(e => e.VideoId == videoId && e.DosyaYolu == "remote/uploaded-audio.m4a"), It.IsAny<CancellationToken>()),
            Times.Once);
        sut.PublishMock.Verify(
            p => p.Publish(It.IsAny<TranscriptReadyEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_WhenCaptionsAreNull_ShouldFallbackToAudioDownloadAndPublishVideoUploaded()
    {
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var video = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "No Captions Video",
            KaynakPlatform = KaynakPlatform.YouTube,
            KaynakUrl = "https://www.youtube.com/watch?v=xyzXYZ98765",
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            IslemDurumu = VideoIslemDurumu.Indiriliyor
        };

        var sut = CreateSut(video);

        sut.RemoteMediaMock
            .Setup(m => m.TryFetchCaptionsAsync(video.KaynakUrl!, "tr", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteCaptionResult?)null);

        sut.RemoteMediaMock
            .Setup(m => m.DownloadAudioAsync(video.KaynakUrl!, It.IsAny<string>(), null, It.IsAny<Action<int, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string dir, string? _, Action<int, string>? _, CancellationToken _) =>
            {
                Directory.CreateDirectory(dir);
                var audioPath = Path.Combine(dir, "fallback.webm");
                File.WriteAllBytes(audioPath, new byte[] { 10, 20, 30, 40 });
                return audioPath;
            });

        sut.FileStorageMock
            .Setup(f => f.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), "audio/webm"))
            .ReturnsAsync("remote/fallback.webm");

        var msg = new RemoteVideoRequestedEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            KaynakUrl = video.KaynakUrl!,
            KaynakPlatform = KaynakPlatform.YouTube,
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            TercihEdilenDil = "tr"
        };

        await sut.Consumer.Consume(CreateContext(msg));

        video.DosyaYolu.Should().Be("remote/fallback.webm");
        video.IslemDurumu.Should().Be(VideoIslemDurumu.Bekliyor);
        sut.QualityEvaluatorMock.Verify(q => q.Evaluate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<bool>()), Times.Never);
        sut.PublishMock.Verify(
            p => p.Publish(It.Is<VideoUploadedEvent>(e => e.VideoId == videoId && e.DosyaYolu == "remote/fallback.webm"), It.IsAny<CancellationToken>()),
            Times.Once);
        sut.PublishMock.Verify(
            p => p.Publish(It.IsAny<TranscriptReadyEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_WhenAlimModuIsDogrudanSesStt_ShouldSkipCaptionsDownloadAudioAndPublishVideoUploaded()
    {
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var video = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "Force STT Video",
            KaynakPlatform = KaynakPlatform.YouTube,
            KaynakUrl = "https://www.youtube.com/watch?v=forceSTT123",
            AlimModu = UzaktanAlimModu.DogrudanSesStt,
            IslemDurumu = VideoIslemDurumu.Indiriliyor
        };

        var sut = CreateSut(video);

        sut.RemoteMediaMock
            .Setup(m => m.DownloadAudioAsync(video.KaynakUrl!, It.IsAny<string>(), "Bearer tok", It.IsAny<Action<int, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string dir, string? _, Action<int, string>? _, CancellationToken _) =>
            {
                Directory.CreateDirectory(dir);
                var audioPath = Path.Combine(dir, "direct.m4a");
                File.WriteAllBytes(audioPath, new byte[] { 7, 8, 9 });
                return audioPath;
            });

        sut.FileStorageMock
            .Setup(f => f.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), "audio/mp4"))
            .ReturnsAsync("remote/direct.m4a");

        var msg = new RemoteVideoRequestedEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            KaynakUrl = video.KaynakUrl!,
            KaynakPlatform = KaynakPlatform.YouTube,
            AlimModu = UzaktanAlimModu.DogrudanSesStt,
            TercihEdilenDil = "tr",
            AuthHeader = "Bearer tok",
            TranscriptUrl = "https://example.com/captions.vtt"
        };

        await sut.Consumer.Consume(CreateContext(msg));

        sut.RemoteMediaMock.Verify(
            m => m.TryFetchCaptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        sut.RemoteMediaMock.Verify(
            m => m.TryFetchTranscriptFromUrlAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        sut.RemoteMediaMock.Verify(
            m => m.DownloadAudioAsync(video.KaynakUrl!, It.IsAny<string>(), "Bearer tok", It.IsAny<Action<int, string>?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        video.DosyaYolu.Should().Be("remote/direct.m4a");
        video.IslemDurumu.Should().Be(VideoIslemDurumu.Bekliyor);

        sut.PublishMock.Verify(
            p => p.Publish(It.Is<VideoUploadedEvent>(e => e.VideoId == videoId && e.DosyaYolu == "remote/direct.m4a"), It.IsAny<CancellationToken>()),
            Times.Once);
        sut.PublishMock.Verify(
            p => p.Publish(It.IsAny<TranscriptReadyEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_WhenDrmProtectedContentExceptionThrown_ShouldMarkVideoHataRecordErrorAndNotPublishFlowEvents()
    {
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var video = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "DRM Protected Video",
            KaynakPlatform = KaynakPlatform.DirectUrl,
            KaynakUrl = "https://protected.example.com/stream.m3u8",
            AlimModu = UzaktanAlimModu.DogrudanSesStt,
            IslemDurumu = VideoIslemDurumu.Indiriliyor
        };

        var sut = CreateSut(video);
        const string drmMessage = "Bu içerik DRM korumalıdır (Widevine/FairPlay) ve indirilemez.";

        sut.RemoteMediaMock
            .Setup(m => m.DownloadAudioAsync(video.KaynakUrl!, It.IsAny<string>(), null, It.IsAny<Action<int, string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DrmProtectedContentException(drmMessage));

        var msg = new RemoteVideoRequestedEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            KaynakUrl = video.KaynakUrl!,
            KaynakPlatform = KaynakPlatform.DirectUrl,
            AlimModu = UzaktanAlimModu.DogrudanSesStt
        };

        Func<Task> act = () => sut.Consumer.Consume(CreateContext(msg));
        await act.Should().ThrowAsync<DrmProtectedContentException>()
            .WithMessage($"*{drmMessage}*");

        video.IslemDurumu.Should().Be(VideoIslemDurumu.Hata);
        video.TranskriptKaynakNotu.Should().Contain("DRM korumalı");

        sut.StateTrackerMock.Verify(
            s => s.RecordErrorAsync(videoId, egitimId, "Uzaktan İndirme", drmMessage),
            Times.Once);

        sut.PublishMock.Verify(
            p => p.Publish(It.IsAny<TranscriptReadyEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        sut.PublishMock.Verify(
            p => p.Publish(It.IsAny<VideoUploadedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        sut.CacheMock.Verify(c => c.RemoveAsync($"lock:download:{videoId}"), Times.Once);
    }

    [Fact]
    public async Task Consume_WhenPreferredLangIsTrButCaptionDilIsEn_WithRealEvaluator_ShouldAcceptFastTranscriptAndSkipAudioDownload()
    {
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var video = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "English Lecture Video",
            KaynakPlatform = KaynakPlatform.YouTube,
            KaynakUrl = "https://www.youtube.com/watch?v=eng12345678",
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            IslemDurumu = VideoIslemDurumu.Indiriliyor
        };

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var videoDbSetMock = new Mock<DbSet<Video>>();
        var transcriptDbSetMock = new Mock<DbSet<VideoTranscript>>();
        var savedTranscripts = new List<VideoTranscript>();

        dbContextMock.Setup(x => x.Videolar).ReturnsDbSet(new List<Video> { video }, videoDbSetMock);
        videoDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object[] keyValues, CancellationToken _) =>
                (Guid)keyValues[0] == video.Id ? video : null);

        dbContextMock.Setup(x => x.VideoTranscripts).ReturnsDbSet(savedTranscripts, transcriptDbSetMock);
        transcriptDbSetMock.Setup(x => x.Add(It.IsAny<VideoTranscript>()))
            .Callback<VideoTranscript>(t => savedTranscripts.Add(t));

        var cacheMock = new Mock<ICacheService>();
        cacheMock.Setup(c => c.SetIfNotExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        var remoteMediaMock = new Mock<IRemoteMediaService>();
        var fileStorageMock = new Mock<IFileStorageService>();
        var publishMock = new Mock<IPublishEndpoint>();
        var stateTrackerMock = new Mock<IPipelineStateTracker>();
        stateTrackerMock
            .Setup(s => s.StartHeartbeat(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new Mock<IAsyncDisposable>().Object);

        var realEvaluator = new TranscriptQualityEvaluator();

        var consumer = new DownloadRemoteVideoConsumer(
            new Mock<ILogger<DownloadRemoteVideoConsumer>>().Object,
            cacheMock.Object,
            remoteMediaMock.Object,
            realEvaluator,
            fileStorageMock.Object,
            dbContextMock.Object,
            publishMock.Object,
            stateTrackerMock.Object);

        var englishCaption =
            "Hello everyone today we will explore software architecture and clean design principles across distributed systems " +
            "Message queues decouple background workers from web controllers and improve overall system resilience under heavy load " +
            "Database schema migrations should remain backward compatible when upgrading standalone desktop installations safely " +
            "Unit tests verify that deterministic business rules behave properly across boundary inputs and failure paths " +
            "Caching layers reduce latency by serving frequently accessed configuration settings directly from memory " +
            "Clear progress notifications in the user interface help operators monitor long running media pipelines";

        remoteMediaMock
            .Setup(m => m.TryFetchCaptionsAsync(video.KaynakUrl!, "tr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteCaptionResult
            {
                HamMetin = englishCaption,
                ZamanDamgalariJson = "[{\"start\":0,\"end\":10,\"text\":\"Hello everyone\"}]",
                Dil = "en",
                IsManual = true,
                SureSaniye = 120
            });

        var msg = new RemoteVideoRequestedEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            KaynakUrl = video.KaynakUrl!,
            KaynakPlatform = KaynakPlatform.YouTube,
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            TercihEdilenDil = "tr"
        };

        await consumer.Consume(CreateContext(msg));

        video.IslemDurumu.Should().Be(VideoIslemDurumu.SttTamamlandi);
        video.TranskriptKaliteSkoru.Should().BeGreaterThanOrEqualTo(70);
        savedTranscripts.Should().ContainSingle();
        savedTranscripts[0].Dil.Should().Be("en");

        publishMock.Verify(
            p => p.Publish(It.Is<TranscriptReadyEvent>(e => e.VideoId == videoId && e.EgitimId == egitimId), It.IsAny<CancellationToken>()),
            Times.Once);
        remoteMediaMock.Verify(
            m => m.DownloadAudioAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Action<int, string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
