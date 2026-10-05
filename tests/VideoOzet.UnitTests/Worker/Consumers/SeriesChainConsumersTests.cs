using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
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

namespace VideoOzet.UnitTests.Worker.Consumers;

public class SeriesChainConsumersTests
{
    [Fact]
    public async Task TopicAnalysisCompletedConsumer_ShouldPublish_SeriesPlanRequestedEvent()
    {
        // Arrange
        var mockPublish = new Mock<IPublishEndpoint>();
        var mockLogger = new Mock<ILogger<TopicAnalysisCompletedConsumer>>();
        var consumer = new TopicAnalysisCompletedConsumer(mockPublish.Object, mockLogger.Object);

        var requestId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();

        var contextMock = new Mock<ConsumeContext<TopicAnalysisCompletedEvent>>();
        contextMock.Setup(c => c.Message).Returns(new TopicAnalysisCompletedEvent
        {
            ContentRequestId = requestId,
            EgitimId = egitimId,
            KonuAnaliziId = Guid.NewGuid()
        });

        SeriesPlanRequestedEvent? publishedEvent = null;
        mockPublish.Setup(p => p.Publish(It.IsAny<SeriesPlanRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SeriesPlanRequestedEvent, CancellationToken>((ev, ct) => publishedEvent = ev)
            .Returns(Task.CompletedTask);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        publishedEvent.Should().NotBeNull();
        publishedEvent!.ContentRequestId.Should().Be(requestId);
        publishedEvent.EgitimId.Should().Be(egitimId);
    }

    [Fact]
    public async Task SeriesVideoGeneratedConsumer_WhenIntermediateVideo_ShouldTriggerNextVideo()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dbContext = new AppDbContext(options);
        var requestId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();

        var request = new ContentRequest { Id = requestId, EgitimId = egitimId, Konu = "Seri" };
        var plan = new SeriPlani
        {
            Id = Guid.NewGuid(),
            ContentRequestId = requestId,
            PlanNo = 1,
            VideoSayisi = 2,
            ContentRequest = request
        };

        var video1 = new SeriBolum
        {
            Id = Guid.NewGuid(),
            SeriPlaniId = plan.Id,
            BolumNo = 1,
            CalismaBasligi = "Video 1",
            Durum = BolumDurumu.Tamamlandi
        };
        var rev1 = new BolumRevizyonu
        {
            Id = Guid.NewGuid(),
            SeriBolumId = video1.Id,
            RevizyonNo = 1,
            ArastirmaOzeti = "Ozet 1",
            VideoPlani = "Plan 1"
        };
        video1.Revizyonlar.Add(rev1);

        var video2 = new SeriBolum
        {
            Id = Guid.NewGuid(),
            SeriPlaniId = plan.Id,
            BolumNo = 2,
            CalismaBasligi = "Video 2",
            Durum = BolumDurumu.Bekliyor
        };

        plan.SeriBolumler.Add(video1);
        plan.SeriBolumler.Add(video2);

        dbContext.ContentRequests.Add(request);
        dbContext.SeriPlanlari.Add(plan);
        dbContext.SeriBolumler.AddRange(video1, video2);
        await dbContext.SaveChangesAsync();

        var mockPublish = new Mock<IPublishEndpoint>();
        var mockLogger = new Mock<ILogger<SeriesVideoGeneratedConsumer>>();

        SeriesVideoGenerationCommand? command = null;
        mockPublish.Setup(p => p.Publish(It.IsAny<SeriesVideoGenerationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<SeriesVideoGenerationCommand, CancellationToken>((cmd, ct) => command = cmd)
            .Returns(Task.CompletedTask);

        var consumer = new SeriesVideoGeneratedConsumer(dbContext, mockPublish.Object, mockLogger.Object);

        var contextMock = new Mock<ConsumeContext<SeriesVideoGeneratedEvent>>();
        contextMock.Setup(c => c.Message).Returns(new SeriesVideoGeneratedEvent
        {
            ContentRequestId = requestId,
            EgitimId = egitimId,
            SeriBolumId = video1.Id,
            BolumRevizyonuId = rev1.Id
        });

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        command.Should().NotBeNull();
        command!.BolumNo.Should().Be(2);
        command.SeriBolumId.Should().Be(video2.Id);

        var updatedVideo2 = await dbContext.SeriBolumler.FindAsync(video2.Id);
        updatedVideo2!.Durum.Should().Be(BolumDurumu.Isleniyor);
    }

    [Fact]
    public async Task SeriesVideoGeneratedConsumer_WhenLastVideo_ShouldCompletePlanAndPublishEvent()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dbContext = new AppDbContext(options);
        var requestId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();

        var request = new ContentRequest { Id = requestId, EgitimId = egitimId, Konu = "Seri", Durum = ContentRequestDurumu.IcerikUretiliyor };
        var plan = new SeriPlani
        {
            Id = Guid.NewGuid(),
            ContentRequestId = requestId,
            PlanNo = 1,
            VideoSayisi = 1,
            ContentRequest = request
        };

        var video1 = new SeriBolum
        {
            Id = Guid.NewGuid(),
            SeriPlaniId = plan.Id,
            BolumNo = 1,
            CalismaBasligi = "Video 1",
            Durum = BolumDurumu.Tamamlandi
        };
        var rev1 = new BolumRevizyonu
        {
            Id = Guid.NewGuid(),
            SeriBolumId = video1.Id,
            RevizyonNo = 1
        };
        video1.Revizyonlar.Add(rev1);
        plan.SeriBolumler.Add(video1);

        dbContext.ContentRequests.Add(request);
        dbContext.SeriPlanlari.Add(plan);
        dbContext.SeriBolumler.Add(video1);
        await dbContext.SaveChangesAsync();

        var mockPublish = new Mock<IPublishEndpoint>();
        var mockLogger = new Mock<ILogger<SeriesVideoGeneratedConsumer>>();

        SeriesPlanCompletedEvent? completedEvent = null;
        mockPublish.Setup(p => p.Publish(It.IsAny<SeriesPlanCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SeriesPlanCompletedEvent, CancellationToken>((ev, ct) => completedEvent = ev)
            .Returns(Task.CompletedTask);

        var consumer = new SeriesVideoGeneratedConsumer(dbContext, mockPublish.Object, mockLogger.Object);

        var contextMock = new Mock<ConsumeContext<SeriesVideoGeneratedEvent>>();
        contextMock.Setup(c => c.Message).Returns(new SeriesVideoGeneratedEvent
        {
            ContentRequestId = requestId,
            EgitimId = egitimId,
            SeriBolumId = video1.Id,
            BolumRevizyonuId = rev1.Id
        });

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        completedEvent.Should().NotBeNull();
        completedEvent!.ContentRequestId.Should().Be(requestId);

        var updatedRequest = await dbContext.ContentRequests.FindAsync(requestId);
        updatedRequest!.Durum.Should().Be(ContentRequestDurumu.Tamamlandi);
        updatedRequest.TamamlanmaTarihi.Should().NotBeNull();
    }

    [Fact]
    public async Task SeriesVideoGenerationConsumer_ShouldRetrieveChunks_AndSaveKullanilanKaynaklar()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new AppDbContext(options);
        var requestId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var bolumId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        var egitim = new Egitim { Id = egitimId, Ad = "Test Egitim" };
        var request = new ContentRequest
        {
            Id = requestId,
            EgitimId = egitimId,
            Konu = "Clean Code",
            HedefKitle = "Yazılımcılar"
        };

        var plan = new SeriPlani
        {
            Id = planId,
            ContentRequestId = requestId,
            PlanNo = 1,
            Durum = SeriPlanDurumu.Onaylandi
        };

        var bolum = new SeriBolum
        {
            Id = bolumId,
            SeriPlaniId = planId,
            BolumNo = 1,
            CalismaBasligi = "İsimlendirme Standartları",
            AnaFikir = "Anlamlı isimler kullanmak kodu temiz kılar",
            KonularJson = "[\"Değişken İsimleri\", \"Fonksiyon İsimleri\"]",
            Durum = BolumDurumu.Bekliyor
        };

        plan.SeriBolumler.Add(bolum);
        dbContext.Egitimler.Add(egitim);
        dbContext.ContentRequests.Add(request);
        dbContext.SeriPlanlari.Add(plan);
        dbContext.SeriBolumler.Add(bolum);
        await dbContext.SaveChangesAsync();

        var mockSynthesis = new Mock<ISynthesisProvider>();
        mockSynthesis.Setup(s => s.GenerateSeriesVideoContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(@"{
                ""ArastirmaOzeti"": ""Temiz kod araştırma özeti"",
                ""VideoPlani"": ""1. Giriş\n2. İsimlendirme"",
                ""DevirNotu"": ""Sonraki bölüm fonksiyon boyutlarına geçmeli""
            }");

        var mockEmbedding = new Mock<IEmbeddingProvider>();
        mockEmbedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync(new float[1536]);

        var mockLogService = new Mock<ILogService>();
        mockLogService.Setup(s => s.LogPipelineStartAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<PipelineAsamasi>(), It.IsAny<string?>()))
            .ReturnsAsync(1L);

        var mockLogger = new Mock<ILogger<SeriesVideoGenerationConsumer>>();

        var dummyChunks = new List<VideoChunkDocument>
        {
            new VideoChunkDocument
            {
                Id = chunkId,
                EgitimId = egitimId,
                VideoId = Guid.NewGuid(),
                Text = "Değişkenler niyetini belli etmeli.",
                StartTimeMs = 1000,
                EndTimeMs = 5000
            }
        };

        var consumer = new TestableSeriesVideoGenerationConsumer(
            dbContext,
            mockSynthesis.Object,
            mockEmbedding.Object,
            mockLogService.Object,
            mockLogger.Object,
            dummyChunks);

        var contextMock = new Mock<ConsumeContext<SeriesVideoGenerationCommand>>();
        contextMock.Setup(c => c.Message).Returns(new SeriesVideoGenerationCommand
        {
            ContentRequestId = requestId,
            EgitimId = egitimId,
            SeriPlaniId = planId,
            SeriBolumId = bolumId,
            BolumNo = 1
        });

        var publishedEvents = new List<object>();
        contextMock.Setup(c => c.Publish(It.IsAny<PipelineProgressEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineProgressEvent, CancellationToken>((ev, ct) => publishedEvents.Add(ev))
            .Returns(Task.CompletedTask);
        contextMock.Setup(c => c.Publish(It.IsAny<SeriesVideoGeneratedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SeriesVideoGeneratedEvent, CancellationToken>((ev, ct) => publishedEvents.Add(ev))
            .Returns(Task.CompletedTask);
        contextMock.Setup(c => c.Publish(It.IsAny<ContentErrorEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ContentErrorEvent, CancellationToken>((ev, ct) => publishedEvents.Add(ev))
            .Returns(Task.CompletedTask);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        var err = publishedEvents.OfType<ContentErrorEvent>().FirstOrDefault();
        err.Should().BeNull(err?.HataMesaji ?? "no error");

        var updatedBolum = await dbContext.SeriBolumler
            .Include(b => b.Revizyonlar)
            .FirstOrDefaultAsync(b => b.Id == bolumId);

        updatedBolum.Should().NotBeNull();
        updatedBolum!.Durum.Should().Be(BolumDurumu.Tamamlandi);
        updatedBolum.Revizyonlar.Should().HaveCount(1);

        var rev = updatedBolum.Revizyonlar.First();
        rev.ArastirmaOzeti.Should().Contain("Temiz kod araştırma özeti");
        rev.KullanilanKaynaklar.Should().Contain(chunkId.ToString());

        publishedEvents.OfType<SeriesVideoGeneratedEvent>().Should().HaveCount(1);
    }

    private class TestableSeriesVideoGenerationConsumer : SeriesVideoGenerationConsumer
    {
        private readonly List<VideoChunkDocument> _chunksToReturn;

        public TestableSeriesVideoGenerationConsumer(
            AppDbContext dbContext,
            ISynthesisProvider synthesisProvider,
            IEmbeddingProvider embeddingProvider,
            ILogService logService,
            ILogger<SeriesVideoGenerationConsumer> logger,
            List<VideoChunkDocument> chunksToReturn)
            : base(dbContext, synthesisProvider, embeddingProvider, logService, logger)
        {
            _chunksToReturn = chunksToReturn;
        }

        protected override Task<List<VideoChunkDocument>> GetRelevantChunksAsync(
            Guid egitimId,
            Pgvector.Vector queryVector,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_chunksToReturn);
        }
    }
}
