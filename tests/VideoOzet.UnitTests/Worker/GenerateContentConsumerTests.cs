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
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Consumers;
using Xunit;

namespace VideoOzet.UnitTests.Worker;

public class GenerateContentConsumerTests
{
    private class TestableGenerateContentConsumer : GenerateContentConsumer
    {
        private readonly List<VideoChunkDocument> _chunksToReturn;

        public TestableGenerateContentConsumer(
            AppDbContext dbContext,
            IEmbeddingProvider embeddingProvider,
            ISynthesisProvider synthesisProvider,
            ILogService logService,
            ILogger<GenerateContentConsumer> logger,
            List<VideoChunkDocument>? chunksToReturn = null)
            : base(dbContext, embeddingProvider, synthesisProvider, logService, logger)
        {
            _chunksToReturn = chunksToReturn ?? new List<VideoChunkDocument>();
        }

        protected override Task<List<VideoChunkDocument>> GetRelevantChunksAsync(
            Guid egitimId, Pgvector.Vector queryVector, CancellationToken cancellationToken)
        {
            return Task.FromResult(_chunksToReturn);
        }
    }

    private readonly Mock<IEmbeddingProvider> _mockEmbeddingProvider;
    private readonly Mock<ISynthesisProvider> _mockSynthesisProvider;
    private readonly Mock<ILogService> _mockLogService;
    private readonly Mock<ILogger<GenerateContentConsumer>> _mockLogger;
    private readonly Mock<ConsumeContext<ContentRequestedEvent>> _mockContext;

    public GenerateContentConsumerTests()
    {
        _mockEmbeddingProvider = new Mock<IEmbeddingProvider>();
        _mockSynthesisProvider = new Mock<ISynthesisProvider>();
        _mockLogService = new Mock<ILogService>();
        _mockLogger = new Mock<ILogger<GenerateContentConsumer>>();
        _mockContext = new Mock<ConsumeContext<ContentRequestedEvent>>();
    }

    [Fact]
    public async Task Consume_ShouldGenerateContentAndPublishEvent_WhenValidRequest()
    {
        // Arrange
        var contentRequestId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var videoId = Guid.NewGuid();

        var request = new ContentRequest
        {
            Id = contentRequestId,
            EgitimId = egitimId,
            Konu = "Clean Architecture",
            HedefUzunluk = "Orta",
            HedefKitle = "Senior Geliştiriciler",
            Durum = ContentRequestDurumu.Bekliyor
        };

        var chunkDoc = new VideoChunkDocument
        {
            Id = Guid.NewGuid(),
            VideoId = videoId,
            EgitimId = egitimId,
            Text = "Clean architecture promotes separation of concerns.",
            StartTimeMs = 1000,
            EndTimeMs = 5000,
            Embedding = new Pgvector.Vector(new float[] { 0.1f, 0.2f, 0.3f })
        };

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var contentRequestsDbSetMock = new Mock<DbSet<ContentRequest>>();
        var videoChunksDbSetMock = new Mock<DbSet<VideoChunkDocument>>();
        var generatedContentsDbSetMock = new Mock<DbSet<GeneratedContent>>();

        dbContextMock.Setup(x => x.ContentRequests).ReturnsDbSet(new List<ContentRequest> { request }, contentRequestsDbSetMock);
        contentRequestsDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync((object[] keyValues) => (Guid)keyValues[0] == contentRequestId ? request : null);
        contentRequestsDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object[] keyValues, CancellationToken ct) => (Guid)keyValues[0] == contentRequestId ? request : null);

        dbContextMock.Setup(x => x.VideoChunkDocuments).ReturnsDbSet(new List<VideoChunkDocument> { chunkDoc }, videoChunksDbSetMock);

        var savedContents = new List<GeneratedContent>();
        dbContextMock.Setup(x => x.GeneratedContents).ReturnsDbSet(savedContents, generatedContentsDbSetMock);
        generatedContentsDbSetMock.Setup(x => x.Add(It.IsAny<GeneratedContent>()))
            .Callback<GeneratedContent>(c => savedContents.Add(c));

        _mockEmbeddingProvider.Setup(e => e.GenerateEmbeddingAsync("Clean Architecture", "text-embedding-3-small"))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        _mockSynthesisProvider.Setup(s => s.GenerateResearchSummaryAsync(
            "Clean Architecture", "Orta", "Senior Geliştiriciler", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Detaylı Araştırma Özeti");

        _mockSynthesisProvider.Setup(s => s.GenerateVideoPlanAsync(
            "Clean Architecture", "Orta", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Video Planı Bölüm 1...");

        _mockLogService.Setup(l => l.LogPipelineStartAsync(null, egitimId, PipelineAsamasi.Sentez, It.IsAny<string>()))
            .ReturnsAsync(100L);

        _mockContext.Setup(c => c.Message).Returns(new ContentRequestedEvent
        {
            ContentRequestId = contentRequestId,
            EgitimId = egitimId,
            Konu = "Clean Architecture",
            HedefUzunluk = "Orta",
            HedefKitle = "Senior Geliştiriciler"
        });
        _mockContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var consumer = new TestableGenerateContentConsumer(
            dbContextMock.Object,
            _mockEmbeddingProvider.Object,
            _mockSynthesisProvider.Object,
            _mockLogService.Object,
            _mockLogger.Object,
            new List<VideoChunkDocument> { chunkDoc });

        // Act
        await consumer.Consume(_mockContext.Object);

        // Assert
        request.Durum.Should().Be(ContentRequestDurumu.QcYapiliyor);
        savedContents.Should().HaveCount(1);
        savedContents[0].ArastirmaOzeti.Should().Be("Detaylı Araştırma Özeti");
        savedContents[0].VideoPlani.Should().Be("Video Planı Bölüm 1...");
        savedContents[0].ContentRequestId.Should().Be(contentRequestId);

        _mockContext.Verify(c => c.Publish(
            It.Is<ContentGeneratedEvent>(e => e.ContentRequestId == contentRequestId && e.EgitimId == egitimId),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockLogService.Verify(l => l.LogPipelineEndAsync(100L, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Consume_ShouldLogWarningAndReturn_WhenContentRequestNotFound()
    {
        // Arrange
        var contentRequestId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var contentRequestsDbSetMock = new Mock<DbSet<ContentRequest>>();

        dbContextMock.Setup(x => x.ContentRequests).ReturnsDbSet(new List<ContentRequest>(), contentRequestsDbSetMock);
        contentRequestsDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync((ContentRequest?)null);
        contentRequestsDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentRequest?)null);

        _mockLogService.Setup(l => l.LogPipelineStartAsync(null, egitimId, PipelineAsamasi.Sentez, It.IsAny<string>()))
            .ReturnsAsync(200L);

        _mockContext.Setup(c => c.Message).Returns(new ContentRequestedEvent
        {
            ContentRequestId = contentRequestId,
            EgitimId = egitimId,
            Konu = "Non-existent"
        });
        _mockContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var consumer = new GenerateContentConsumer(
            dbContextMock.Object,
            _mockEmbeddingProvider.Object,
            _mockSynthesisProvider.Object,
            _mockLogService.Object,
            _mockLogger.Object);

        // Act
        await consumer.Consume(_mockContext.Object);

        // Assert
        _mockLogService.Verify(l => l.LogPipelineErrorAsync(200L, It.IsAny<Exception>()), Times.Once);
        _mockSynthesisProvider.Verify(s => s.GenerateResearchSummaryAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_ShouldSetStatusToHata_WhenExceptionOccurs()
    {
        // Arrange
        var contentRequestId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();

        var request = new ContentRequest
        {
            Id = contentRequestId,
            EgitimId = egitimId,
            Konu = "Failing Subject",
            Durum = ContentRequestDurumu.Bekliyor
        };

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var contentRequestsDbSetMock = new Mock<DbSet<ContentRequest>>();

        dbContextMock.Setup(x => x.ContentRequests).ReturnsDbSet(new List<ContentRequest> { request }, contentRequestsDbSetMock);
        contentRequestsDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync((object[] keyValues) => (Guid)keyValues[0] == contentRequestId ? request : null);
        contentRequestsDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object[] keyValues, CancellationToken ct) => (Guid)keyValues[0] == contentRequestId ? request : null);

        _mockEmbeddingProvider.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Embedding API failed"));

        _mockLogService.Setup(l => l.LogPipelineStartAsync(null, egitimId, PipelineAsamasi.Sentez, It.IsAny<string>()))
            .ReturnsAsync(300L);

        _mockContext.Setup(c => c.Message).Returns(new ContentRequestedEvent
        {
            ContentRequestId = contentRequestId,
            EgitimId = egitimId,
            Konu = "Failing Subject"
        });
        _mockContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var consumer = new GenerateContentConsumer(
            dbContextMock.Object,
            _mockEmbeddingProvider.Object,
            _mockSynthesisProvider.Object,
            _mockLogService.Object,
            _mockLogger.Object);

        // Act
        await consumer.Consume(_mockContext.Object);

        // Assert
        request.Durum.Should().Be(ContentRequestDurumu.Hata);
        _mockLogService.Verify(l => l.LogFunctionErrorAsync(nameof(GenerateContentConsumer), It.IsAny<Exception>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Once);
        _mockLogService.Verify(l => l.LogPipelineErrorAsync(300L, It.IsAny<Exception>()), Times.Once);
    }
}
