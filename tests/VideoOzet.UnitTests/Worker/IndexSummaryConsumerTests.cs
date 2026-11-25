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

public class IndexSummaryConsumerTests
{
    [Fact]
    public async Task Consume_ShouldChunkEmbedAndSave_WhenSummaryExists()
    {
        // Arrange
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var summaryId = Guid.NewGuid();

        var summary = new VideoSummary
        {
            Id = summaryId,
            VideoId = videoId,
            OzetMetni = "Mock JSON Summary",
            KonuBasliklari = "[]",
            KonuEtiketleri = "[]",
            LlmModel = "mock-model"
        };

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var summariesDbSetMock = new Mock<DbSet<VideoSummary>>();
        var documentsDbSetMock = new Mock<DbSet<VideoChunkDocument>>();

        dbContextMock.Setup(x => x.VideoSummaries).ReturnsDbSet(new List<VideoSummary> { summary }, summariesDbSetMock);
        
        // Mock AddRange behavior
        var savedDocuments = new List<VideoChunkDocument>();
        documentsDbSetMock.Setup(x => x.AddRange(It.IsAny<IEnumerable<VideoChunkDocument>>()))
            .Callback<IEnumerable<VideoChunkDocument>>(docs => savedDocuments.AddRange(docs));
        dbContextMock.Setup(x => x.VideoChunkDocuments).ReturnsDbSet(savedDocuments, documentsDbSetMock);

        var loggerMock = new Mock<ILogger<IndexSummaryConsumer>>();
        var chunkerMock = new Mock<ITextChunker>();
        var embeddingProviderMock = new Mock<IEmbeddingProvider>();

        // Setup chunks
        chunkerMock.Setup(c => c.ChunkText(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(new List<string> { "chunk 1", "chunk 2" });

        // Setup embeddings
        embeddingProviderMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
            .ReturnsAsync(new List<float[]> { new float[] { 0.1f }, new float[] { 0.2f } });

        var consumer = new IndexSummaryConsumer(
            loggerMock.Object,
            dbContextMock.Object,
            chunkerMock.Object,
            embeddingProviderMock.Object);

        var contextMock = new Mock<ConsumeContext<SummaryReadyEvent>>();
        contextMock.Setup(c => c.Message).Returns(new SummaryReadyEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            SummaryId = summaryId
        });

        var publishedEvents = new List<object>();
        contextMock.Setup(c => c.Publish(It.IsAny<PipelineProgressEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineProgressEvent, CancellationToken>((msg, ct) => publishedEvents.Add(msg))
            .Returns(Task.CompletedTask);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        chunkerMock.Verify(c => c.ChunkText("Mock JSON Summary", 500), Times.Once);
        embeddingProviderMock.Verify(e => e.GenerateEmbeddingsAsync(It.Is<List<string>>(l => l.Count == 2), "text-embedding-3-small"), Times.Once);

        savedDocuments.Should().HaveCount(2);
        savedDocuments[0].Text.Should().Be("chunk 1");
        savedDocuments[0].Embedding!.ToArray()[0].Should().Be(0.1f);
        savedDocuments[1].Text.Should().Be("chunk 2");

        dbContextMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        publishedEvents.Should().HaveCount(2); // Basladi and Tamamlandi PipelineProgressEvent
        publishedEvents[1].Should().BeOfType<PipelineProgressEvent>();
        ((PipelineProgressEvent)publishedEvents[1]).Durum.Should().Be("Tamamlandi");
    }

    [Fact]
    public async Task Consume_ShouldIndexRawTranscriptWithoutSummary_WhenVideoIsHamKaynakAndSummaryIdIsEmpty()
    {
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();

        var video = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "Ham Kaynak Test Videosu",
            IndekslemeTuru = VideoOzet.Data.Enums.IndekslemeTuru.HamKaynak,
            IslemDurumu = VideoOzet.Data.Enums.VideoIslemDurumu.OzetlemeTamamlandi
        };

        var transcript = new VideoTranscript
        {
            Id = Guid.NewGuid(),
            VideoId = videoId,
            HamMetin = "Bu bir ham transkript test metnidir ve özet olmadan indekslenir.",
            Dil = "tr"
        };

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var videosDbSetMock = new Mock<DbSet<Video>>();
        var transcriptsDbSetMock = new Mock<DbSet<VideoTranscript>>();
        var summariesDbSetMock = new Mock<DbSet<VideoSummary>>();
        var documentsDbSetMock = new Mock<DbSet<VideoChunkDocument>>();

        dbContextMock.Setup(x => x.Videolar).ReturnsDbSet(new List<Video> { video }, videosDbSetMock);
        videosDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object[] keys, CancellationToken _) => (Guid)keys[0] == videoId ? video : null);
        dbContextMock.Setup(x => x.VideoTranscripts).ReturnsDbSet(new List<VideoTranscript> { transcript }, transcriptsDbSetMock);
        dbContextMock.Setup(x => x.VideoSummaries).ReturnsDbSet(new List<VideoSummary>(), summariesDbSetMock);

        var savedDocuments = new List<VideoChunkDocument>();
        documentsDbSetMock.Setup(x => x.AddRange(It.IsAny<IEnumerable<VideoChunkDocument>>()))
            .Callback<IEnumerable<VideoChunkDocument>>(docs => savedDocuments.AddRange(docs));
        dbContextMock.Setup(x => x.VideoChunkDocuments).ReturnsDbSet(savedDocuments, documentsDbSetMock);

        var chunkerMock = new Mock<ITextChunker>();
        chunkerMock.Setup(c => c.ChunkText(transcript.HamMetin, 500))
            .Returns(new List<string> { "ham parça 1" });

        var embeddingProviderMock = new Mock<IEmbeddingProvider>();
        embeddingProviderMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
            .ReturnsAsync(new List<float[]> { new float[] { 0.42f } });

        var consumer = new IndexSummaryConsumer(
            new Mock<ILogger<IndexSummaryConsumer>>().Object,
            dbContextMock.Object,
            chunkerMock.Object,
            embeddingProviderMock.Object);

        var contextMock = new Mock<ConsumeContext<SummaryReadyEvent>>();
        contextMock.Setup(c => c.Message).Returns(new SummaryReadyEvent
        {
            VideoId = videoId,
            EgitimId = egitimId,
            SummaryId = Guid.Empty
        });
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        contextMock.Setup(c => c.Publish(It.IsAny<PipelineProgressEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await consumer.Consume(contextMock.Object);

        chunkerMock.Verify(c => c.ChunkText(transcript.HamMetin, 500), Times.Once);
        savedDocuments.Should().ContainSingle();
        savedDocuments[0].ChunkTipi.Should().Be(VideoOzet.Data.Enums.ChunkTipi.Transkript);
        savedDocuments[0].Text.Should().Be("ham parça 1");
        video.IslemDurumu.Should().Be(VideoOzet.Data.Enums.VideoIslemDurumu.Tamamlandi);
    }
}
