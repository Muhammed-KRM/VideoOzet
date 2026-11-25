using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Worker.Consumers;
using Xunit;

namespace VideoOzet.UnitTests.Worker;

public class SummarizeVideoConsumerTests
{
    [Fact]
    public async Task Consume_ShouldSummarizeAndSave_WhenTranscriptExists()
    {
        // Arrange
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var transcriptId = Guid.NewGuid();

        var transcript = new VideoTranscript
        {
            Id = transcriptId,
            VideoId = videoId,
            HamMetin = "Test metni",
            SttModel = "mock-model"
        };

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var transcriptsDbSetMock = new Mock<DbSet<VideoTranscript>>();
        var summariesDbSetMock = new Mock<DbSet<VideoSummary>>();

        dbContextMock.Setup(x => x.VideoTranscripts).ReturnsDbSet(new List<VideoTranscript> { transcript }, transcriptsDbSetMock);
        
        // Mock Add behavior for VideoSummaries
        var savedSummaries = new List<VideoSummary>();
        summariesDbSetMock.Setup(x => x.Add(It.IsAny<VideoSummary>())).Callback<VideoSummary>(s => savedSummaries.Add(s));
        dbContextMock.Setup(x => x.VideoSummaries).ReturnsDbSet(savedSummaries, summariesDbSetMock);

        // We also need to mock FindAsync or just use the setup provided by ReturnsDbSet for LINQ
        // Wait, consumer uses FirstOrDefaultAsync, which ReturnsDbSet supports.

        var loggerMock = new Mock<ILogger<SummarizeVideoConsumer>>();
        var geminiProviderMock = new Mock<IGeminiProvider>();
        geminiProviderMock.Setup(g => g.SummarizeAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Mock JSON Summary");

        var publishEndpointMock = new Mock<IPublishEndpoint>();
        
        var consumer = new SummarizeVideoConsumer(
            loggerMock.Object,
            dbContextMock.Object,
            geminiProviderMock.Object,
            publishEndpointMock.Object);

        var contextMock = new Mock<ConsumeContext<TranscriptReadyEvent>>();
        contextMock.Setup(c => c.Message).Returns(new TranscriptReadyEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            TranscriptId = transcriptId
        });
        
        // Setup Publish for context to track events published during Consume
        var publishedEvents = new List<object>();
        
        publishEndpointMock.Setup(c => c.Publish(It.IsAny<PipelineProgressEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineProgressEvent, CancellationToken>((msg, ct) => publishedEvents.Add(msg))
            .Returns(Task.CompletedTask);
            
        contextMock.Setup(c => c.Publish(It.IsAny<SummaryReadyEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SummaryReadyEvent, CancellationToken>((msg, ct) => publishedEvents.Add(msg))
            .Returns(Task.CompletedTask);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        geminiProviderMock.Verify(g => g.SummarizeAsync("Test metni", "gemini-1.5-flash"), Times.Once);
        
        savedSummaries.Should().HaveCount(1);
        savedSummaries[0].VideoId.Should().Be(videoId);
        savedSummaries[0].OzetMetni.Should().Be("Mock JSON Summary");

        dbContextMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // It should publish PipelineProgressEvent (queued), PipelineProgressEvent (started), SummaryReadyEvent, PipelineProgressEvent (completed)
        publishedEvents.Should().HaveCount(4);
        publishedEvents[2].Should().BeOfType<SummaryReadyEvent>();
        ((SummaryReadyEvent)publishedEvents[2]).VideoId.Should().Be(videoId);
    }

    [Fact]
    public async Task Consume_ShouldSkipLlmSummarizationAndPublishSummaryReady_WhenVideoIsHamKaynak()
    {
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var transcriptId = Guid.NewGuid();

        var transcript = new VideoTranscript
        {
            Id = transcriptId,
            VideoId = videoId,
            HamMetin = "Özgür irade ve ahlaki sorumluluk üzerine Türkçe ders transkripti.",
            Dil = "tr",
            SttModel = "youtube-captions-auto"
        };

        var video = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "Ham Kaynak Video",
            IndekslemeTuru = VideoOzet.Data.Enums.IndekslemeTuru.HamKaynak,
            IslemDurumu = VideoOzet.Data.Enums.VideoIslemDurumu.SttTamamlandi
        };

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var transcriptsDbSetMock = new Mock<DbSet<VideoTranscript>>();
        var videosDbSetMock = new Mock<DbSet<Video>>();
        var summariesDbSetMock = new Mock<DbSet<VideoSummary>>();

        dbContextMock.Setup(x => x.VideoTranscripts).ReturnsDbSet(new List<VideoTranscript> { transcript }, transcriptsDbSetMock);
        dbContextMock.Setup(x => x.Videolar).ReturnsDbSet(new List<Video> { video }, videosDbSetMock);
        videosDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object[] keys, CancellationToken _) => (Guid)keys[0] == videoId ? video : null);
        dbContextMock.Setup(x => x.VideoSummaries).ReturnsDbSet(new List<VideoSummary>(), summariesDbSetMock);

        var geminiProviderMock = new Mock<IGeminiProvider>();
        var publishEndpointMock = new Mock<IPublishEndpoint>();
        publishEndpointMock.Setup(c => c.Publish(It.IsAny<PipelineProgressEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = new SummarizeVideoConsumer(
            new Mock<ILogger<SummarizeVideoConsumer>>().Object,
            dbContextMock.Object,
            geminiProviderMock.Object,
            publishEndpointMock.Object);

        var publishedSummaryEvents = new List<SummaryReadyEvent>();
        var contextMock = new Mock<ConsumeContext<TranscriptReadyEvent>>();
        contextMock.Setup(c => c.Message).Returns(new TranscriptReadyEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            TranscriptId = transcriptId
        });
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        contextMock.Setup(c => c.Publish(It.IsAny<SummaryReadyEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SummaryReadyEvent, CancellationToken>((msg, _) => publishedSummaryEvents.Add(msg))
            .Returns(Task.CompletedTask);

        await consumer.Consume(contextMock.Object);

        geminiProviderMock.Verify(g => g.SummarizeAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        summariesDbSetMock.Verify(s => s.Add(It.IsAny<VideoSummary>()), Times.Never);
        publishedSummaryEvents.Should().ContainSingle();
        publishedSummaryEvents[0].VideoId.Should().Be(videoId);
        publishedSummaryEvents[0].SummaryId.Should().Be(Guid.Empty);
    }
}
