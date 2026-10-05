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
}
