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

namespace VideoOzet.UnitTests.Worker.Consumers;

public class TopicAnalysisConsumerTests
{
    [Fact]
    public async Task Consume_ShouldAnalyzeTopicAndPublishCompletedEvent()
    {
        // Arrange
        var contentRequestId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();

        var request = new ContentRequest
        {
            Id = contentRequestId,
            EgitimId = egitimId,
            Konu = "Microservices"
        };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
            
        var dbContext = new AppDbContext(options);
        
        dbContext.Egitimler.Add(new Egitim { Id = egitimId, Ad = "Test Egitim" });
        dbContext.ContentRequests.Add(request);
        await dbContext.SaveChangesAsync();

        var mockLogger = new Mock<ILogger<TopicAnalysisConsumer>>();
        var mockLogService = new Mock<ILogService>();
        
        var mockSynthesisProvider = new Mock<ISynthesisProvider>();
        var jsonResult = @"{ ""AnaFikir"": ""Test Fikir"", ""OnerilenVideoSayisi"": 2, ""OneriSuresiDk"": 15 }";
        mockSynthesisProvider.Setup(x => x.AnalyzeTopicAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResult);

        var mockTopicMapper = new Mock<ISourceTopicMapper>();
        mockTopicMapper.Setup(x => x.BuildTopicDigestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Digest Content");

        var consumer = new TopicAnalysisConsumer(
            dbContext,
            mockSynthesisProvider.Object,
            mockTopicMapper.Object,
            mockLogService.Object,
            mockLogger.Object);

        var contextMock = new Mock<ConsumeContext<TopicAnalysisRequestedEvent>>();
        contextMock.Setup(c => c.Message).Returns(new TopicAnalysisRequestedEvent
        {
            ContentRequestId = contentRequestId,
            EgitimId = egitimId
        });
        
        var publishedEvents = new List<object>();
        contextMock.Setup(c => c.Publish(It.IsAny<PipelineProgressEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineProgressEvent, CancellationToken>((msg, ct) => publishedEvents.Add(msg))
            .Returns(Task.CompletedTask);
        contextMock.Setup(c => c.Publish(It.IsAny<TopicAnalysisCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<TopicAnalysisCompletedEvent, CancellationToken>((msg, ct) => publishedEvents.Add(msg))
            .Returns(Task.CompletedTask);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        var savedAnalizler = await dbContext.KonuAnalizleri.ToListAsync();
        savedAnalizler.Should().HaveCount(1);
        var analiz = savedAnalizler[0];
        analiz.AnaFikir.Should().Be("Test Fikir");
        analiz.OnerilenVideoSayisi.Should().Be(2);
        analiz.OneriSuresiDk.Should().Be(15);
        analiz.Durum.Should().Be(AnalizDurumu.Tamamlandi);

        // Should publish PipelineProgressEvent (Basladi), PipelineProgressEvent (Tamamlandi), and TopicAnalysisCompletedEvent
        publishedEvents.Should().HaveCount(3);
        publishedEvents[2].Should().BeOfType<TopicAnalysisCompletedEvent>();
    }
}
