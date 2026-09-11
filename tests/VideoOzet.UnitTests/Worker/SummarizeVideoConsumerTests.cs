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

        var consumer = new SummarizeVideoConsumer(
            loggerMock.Object,
            dbContextMock.Object,
            geminiProviderMock.Object);

        var contextMock = new Mock<ConsumeContext<TranscriptReadyEvent>>();
        contextMock.Setup(c => c.Message).Returns(new TranscriptReadyEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            TranscriptId = transcriptId
        });
        
        // Setup Publish for context to track events published during Consume
        var publishedEvents = new List<object>();
        contextMock.Setup(c => c.Publish(It.IsAny<PipelineProgressEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineProgressEvent, CancellationToken>((msg, ct) => publishedEvents.Add(msg))
            .Returns(Task.CompletedTask);
            
        contextMock.Setup(c => c.Publish(It.IsAny<SummaryReadyEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SummaryReadyEvent, CancellationToken>((msg, ct) => publishedEvents.Add(msg))
            .Returns(Task.CompletedTask);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        geminiProviderMock.Verify(g => g.SummarizeAsync("Test metni", "gemini-3.5-flash"), Times.Once);
        
        savedSummaries.Should().HaveCount(1);
        savedSummaries[0].VideoId.Should().Be(videoId);
        savedSummaries[0].OzetMetni.Should().Be("Mock JSON Summary");

        dbContextMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // It should publish PipelineProgressEvent (started), SummaryReadyEvent, PipelineProgressEvent (completed)
        publishedEvents.Should().HaveCount(3);
        publishedEvents[1].Should().BeOfType<SummaryReadyEvent>();
        ((SummaryReadyEvent)publishedEvents[1]).VideoId.Should().Be(videoId);
    }
}
