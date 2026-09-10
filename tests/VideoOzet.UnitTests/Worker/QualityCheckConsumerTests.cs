using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
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

public class QualityCheckConsumerTests
{
    private readonly Mock<ISynthesisProvider> _mockSynthesisProvider;
    private readonly Mock<ILogService> _mockLogService;
    private readonly Mock<ILogger<QualityCheckConsumer>> _mockLogger;
    private readonly Mock<ConsumeContext<ContentGeneratedEvent>> _mockContext;

    public QualityCheckConsumerTests()
    {
        _mockSynthesisProvider = new Mock<ISynthesisProvider>();
        _mockLogService = new Mock<ILogService>();
        _mockLogger = new Mock<ILogger<QualityCheckConsumer>>();
        _mockContext = new Mock<ConsumeContext<ContentGeneratedEvent>>();
    }

    [Fact]
    public async Task Consume_ShouldPerformQualityCheckAndPublishContentReadyEvent_WhenValidRequest()
    {
        // Arrange
        var contentRequestId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var chunkId1 = Guid.NewGuid();
        var chunkId2 = Guid.NewGuid();

        var request = new ContentRequest
        {
            Id = contentRequestId,
            EgitimId = egitimId,
            Konu = "Microservice Patterns",
            Durum = ContentRequestDurumu.IcerikUretiliyor
        };

        var generatedContent = new GeneratedContent
        {
            Id = Guid.NewGuid(),
            ContentRequestId = contentRequestId,
            ArastirmaOzeti = "- Claim 1: Microservices isolate faults.\n- Claim 2: Monoliths are faster.\n- Claim 3: Node is multi-threaded.",
            VideoPlani = "Video Planı...",
            KullanilanKaynaklar = $"{chunkId1},{chunkId2}"
        };
        request.GeneratedContent = generatedContent;

        var chunks = new List<VideoChunkDocument>
        {
            new VideoChunkDocument { Id = chunkId1, VideoId = Guid.NewGuid(), EgitimId = egitimId, Text = "Microservices help isolate faults." },
            new VideoChunkDocument { Id = chunkId2, VideoId = Guid.NewGuid(), EgitimId = egitimId, Text = "Monoliths are easier initially." }
        };

        var savedQcResults = new List<QcResult>();

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var contentRequestsDbSetMock = new Mock<DbSet<ContentRequest>>();
        var videoChunksDbSetMock = new Mock<DbSet<VideoChunkDocument>>();
        var qcResultsDbSetMock = new Mock<DbSet<QcResult>>();

        dbContextMock.Setup(x => x.ContentRequests).ReturnsDbSet(new List<ContentRequest> { request }, contentRequestsDbSetMock);
        contentRequestsDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync((object[] keyValues) => (Guid)keyValues[0] == contentRequestId ? request : null);
        contentRequestsDbSetMock.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object[] keyValues, CancellationToken ct) => (Guid)keyValues[0] == contentRequestId ? request : null);

        dbContextMock.Setup(x => x.VideoChunkDocuments).ReturnsDbSet(chunks, videoChunksDbSetMock);

        dbContextMock.Setup(x => x.QcResults).ReturnsDbSet(savedQcResults, qcResultsDbSetMock);
        qcResultsDbSetMock.Setup(x => x.Add(It.IsAny<QcResult>()))
            .Callback<QcResult>(qc => savedQcResults.Add(qc));

        // Mock LLM Claim extraction
        _mockSynthesisProvider.Setup(s => s.ExtractClaimsAsync(generatedContent.ArastirmaOzeti, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Claim 1: Microservices isolate faults.\nClaim 2: Monoliths are faster.\nClaim 3: Node is multi-threaded.");

        // Mock Verification
        _mockSynthesisProvider.Setup(s => s.VerifyClaimAsync(
            It.Is<string>(c => c.Contains("Claim 1")), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"durum\": \"desteklendi\", \"aciklama\": \"Kaynakta doğrulanıyor.\"}");

        _mockSynthesisProvider.Setup(s => s.VerifyClaimAsync(
            It.Is<string>(c => c.Contains("Claim 2")), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"durum\": \"belirsiz\", \"aciklama\": \"Kesin kanıt yok.\"}");

        _mockSynthesisProvider.Setup(s => s.VerifyClaimAsync(
            It.Is<string>(c => c.Contains("Claim 3")), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"durum\": \"desteklenmedi\", \"aciklama\": \"Node tek iş parçacıklı event-loop kullanır.\"}");

        _mockLogService.Setup(l => l.LogPipelineStartAsync(null, egitimId, PipelineAsamasi.KaliteKontrol, It.IsAny<string>()))
            .ReturnsAsync(500L);

        _mockContext.Setup(c => c.Message).Returns(new ContentGeneratedEvent
        {
            ContentRequestId = contentRequestId,
            EgitimId = egitimId,
            Konu = "Microservice Patterns"
        });
        _mockContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var consumer = new QualityCheckConsumer(
            dbContextMock.Object,
            _mockSynthesisProvider.Object,
            _mockLogService.Object,
            _mockLogger.Object);

        // Act
        await consumer.Consume(_mockContext.Object);

        // Assert
        request.Durum.Should().Be(ContentRequestDurumu.Tamamlandi);
        request.TamamlanmaTarihi.Should().NotBeNull();

        savedQcResults.Should().HaveCount(1);
        var qc = savedQcResults[0];
        qc.ContentRequestId.Should().Be(contentRequestId);
        qc.ToplamIddiaSayisi.Should().Be(3);
        qc.DesteklenenSayisi.Should().Be(1);
        qc.BelirsizSayisi.Should().Be(1);
        qc.DesteklenmeyenSayisi.Should().Be(1);
        qc.GuvenSkorYuzde.Should().BeApproximately(33.33m, 0.01m);
        qc.DetayliRapor.Should().Contain("desteklendi");
        qc.DetayliRapor.Should().Contain("belirsiz");
        qc.DetayliRapor.Should().Contain("desteklenmedi");

        _mockContext.Verify(c => c.Publish(
            It.Is<ContentReadyEvent>(e => e.ContentRequestId == contentRequestId && e.EgitimId == egitimId),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockLogService.Verify(l => l.LogPipelineEndAsync(500L, It.Is<string>(s => s.Contains("33.33"))), Times.Once);
    }

    [Fact]
    public async Task Consume_ShouldHandleModelReturningInvalidJson_Gracefully()
    {
        // Arrange
        var contentRequestId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();

        var request = new ContentRequest
        {
            Id = contentRequestId,
            EgitimId = egitimId,
            Konu = "Edge Cases",
            Durum = ContentRequestDurumu.IcerikUretiliyor
        };

        var generatedContent = new GeneratedContent
        {
            Id = Guid.NewGuid(),
            ContentRequestId = contentRequestId,
            ArastirmaOzeti = "Simple claim to verify",
            KullanilanKaynaklar = ""
        };
        request.GeneratedContent = generatedContent;

        var savedQcResults = new List<QcResult>();

        var dbContextMock = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        var contentRequestsDbSetMock = new Mock<DbSet<ContentRequest>>();
        var videoChunksDbSetMock = new Mock<DbSet<VideoChunkDocument>>();
        var qcResultsDbSetMock = new Mock<DbSet<QcResult>>();

        dbContextMock.Setup(x => x.ContentRequests).ReturnsDbSet(new List<ContentRequest> { request }, contentRequestsDbSetMock);
        dbContextMock.Setup(x => x.VideoChunkDocuments).ReturnsDbSet(new List<VideoChunkDocument>(), videoChunksDbSetMock);
        dbContextMock.Setup(x => x.QcResults).ReturnsDbSet(savedQcResults, qcResultsDbSetMock);
        qcResultsDbSetMock.Setup(x => x.Add(It.IsAny<QcResult>()))
            .Callback<QcResult>(qc => savedQcResults.Add(qc));

        _mockSynthesisProvider.Setup(s => s.ExtractClaimsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Single claim");

        // Model returns invalid non-JSON string
        _mockSynthesisProvider.Setup(s => s.VerifyClaimAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("I am unable to verify this in JSON format.");

        _mockLogService.Setup(l => l.LogPipelineStartAsync(null, egitimId, PipelineAsamasi.KaliteKontrol, It.IsAny<string>()))
            .ReturnsAsync(501L);

        _mockContext.Setup(c => c.Message).Returns(new ContentGeneratedEvent
        {
            ContentRequestId = contentRequestId,
            EgitimId = egitimId,
            Konu = "Edge Cases"
        });
        _mockContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var consumer = new QualityCheckConsumer(
            dbContextMock.Object,
            _mockSynthesisProvider.Object,
            _mockLogService.Object,
            _mockLogger.Object);

        // Act
        await consumer.Consume(_mockContext.Object);

        // Assert
        request.Durum.Should().Be(ContentRequestDurumu.Tamamlandi);
        savedQcResults.Should().HaveCount(1);
        savedQcResults[0].BelirsizSayisi.Should().Be(1);
        savedQcResults[0].DesteklenenSayisi.Should().Be(0);
        savedQcResults[0].GuvenSkorYuzde.Should().Be(0m);
    }

    [Fact]
    public async Task Consume_ShouldSetStatusToHata_WhenContentRequestNotFound()
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

        _mockLogService.Setup(l => l.LogPipelineStartAsync(null, egitimId, PipelineAsamasi.KaliteKontrol, It.IsAny<string>()))
            .ReturnsAsync(502L);

        _mockContext.Setup(c => c.Message).Returns(new ContentGeneratedEvent
        {
            ContentRequestId = contentRequestId,
            EgitimId = egitimId,
            Konu = "Missing Request"
        });
        _mockContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var consumer = new QualityCheckConsumer(
            dbContextMock.Object,
            _mockSynthesisProvider.Object,
            _mockLogService.Object,
            _mockLogger.Object);

        // Act
        await consumer.Consume(_mockContext.Object);

        // Assert
        _mockContext.Verify(c => c.Publish(It.IsAny<ContentReadyEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockLogService.Verify(l => l.LogFunctionErrorAsync(nameof(QualityCheckConsumer), It.IsAny<Exception>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Once);
        _mockLogService.Verify(l => l.LogPipelineErrorAsync(502L, It.IsAny<Exception>()), Times.Once);
    }
}
