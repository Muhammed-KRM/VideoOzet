using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.EntityFrameworkCore;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Consumers;
using Xunit;

namespace VideoOzet.UnitTests.Worker;

public class ConsumerResilienceTests
{
    private readonly Mock<ILogger<ExtractTranscriptConsumer>> _mockLogger;
    private readonly Mock<ICacheService> _mockCache;
    private readonly Mock<IFileStorageService> _mockStorage;
    private readonly Mock<IAudioExtractor> _mockAudio;
    private readonly Mock<ISttProvider> _mockStt;
    private readonly Mock<IPublishEndpoint> _mockPublish;

    public ConsumerResilienceTests()
    {
        _mockLogger = new Mock<ILogger<ExtractTranscriptConsumer>>();
        _mockCache = new Mock<ICacheService>();
        _mockStorage = new Mock<IFileStorageService>();
        _mockAudio = new Mock<IAudioExtractor>();
        _mockStt = new Mock<ISttProvider>();
        _mockPublish = new Mock<IPublishEndpoint>();
    }

    [Fact]
    public async Task Consume_WhenSttProviderFails_ShouldSetStatusToHata_RemoveRedisLock_AndRethrow()
    {
        // Arrange
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var lockKey = $"lock:stt:{videoId}";

        var video = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "Error Test Video",
            DosyaYolu = "videos/error_test.mp4",
            DosyaBoyutu = 1000,
            OlusturmaTarihi = DateTime.UtcNow,
            IslemDurumu = VideoIslemDurumu.Bekliyor
        };

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var videoDbSetMock = new Mock<DbSet<Video>>();
        dbContextMock.Setup(x => x.Videolar).ReturnsDbSet(new List<Video> { video }, videoDbSetMock);
        videoDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object[] keyValues, CancellationToken token) =>
                (Guid)keyValues[0] == videoId ? video : null);

        _mockCache.Setup(c => c.SetIfNotExistsAsync(lockKey, "1", It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        _mockStorage.Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync(new MemoryStream(new byte[] { 1, 2, 3 }));

        _mockAudio.Setup(a => a.ExtractAudioAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((videoPath, audioPath, token) =>
            {
                var dir = Path.GetDirectoryName(audioPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(audioPath, new byte[] { 1, 2, 3 });
            })
            .ReturnsAsync(true);

        // Simulate external AI provider 504 Gateway Timeout or 429 Rate Limit error
        _mockStt.Setup(s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("504 Gateway Timeout", null, HttpStatusCode.GatewayTimeout));

        var consumer = new ExtractTranscriptConsumer(
            _mockLogger.Object,
            _mockCache.Object,
            _mockStorage.Object,
            _mockAudio.Object,
            _mockStt.Object,
            dbContextMock.Object,
            _mockPublish.Object);

        var contextMock = new Mock<ConsumeContext<VideoUploadedEvent>>();
        contextMock.Setup(c => c.Message).Returns(new VideoUploadedEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            DosyaYolu = "videos/error_test.mp4"
        });
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act & Assert: Exception must be rethrown for MassTransit retry policy
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await consumer.Consume(contextMock.Object);
        });

        // Verify: Video status must be transitioned to Hata
        video.IslemDurumu.Should().Be(VideoIslemDurumu.Hata);

        // Verify: Redis lock must be removed to allow subsequent retries
        _mockCache.Verify(c => c.RemoveAsync(lockKey), Times.Once);

        // Verify: DbContext.SaveChangesAsync was called to persist Hata state
        dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Consume_WhenAlreadyLocked_ShouldSkipProcessingImmediately()
    {
        // Arrange
        var videoId = Guid.NewGuid();
        var lockKey = $"lock:stt:{videoId}";

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());

        _mockCache.Setup(c => c.SetIfNotExistsAsync(lockKey, "1", It.IsAny<TimeSpan>()))
            .ReturnsAsync(false); // Lock already held by another consumer

        var consumer = new ExtractTranscriptConsumer(
            _mockLogger.Object,
            _mockCache.Object,
            _mockStorage.Object,
            _mockAudio.Object,
            _mockStt.Object,
            dbContextMock.Object,
            _mockPublish.Object);

        var contextMock = new Mock<ConsumeContext<VideoUploadedEvent>>();
        contextMock.Setup(c => c.Message).Returns(new VideoUploadedEvent
        {
            VideoId = videoId,
            EgitimId = Guid.NewGuid(),
            DosyaYolu = "videos/dup.mp4"
        });

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert: No download or STT should be attempted
        _mockStorage.Verify(s => s.DownloadFileAsync(It.IsAny<string>()), Times.Never);
        _mockStt.Verify(s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockPublish.Verify(p => p.Publish(It.IsAny<TranscriptReadyEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
