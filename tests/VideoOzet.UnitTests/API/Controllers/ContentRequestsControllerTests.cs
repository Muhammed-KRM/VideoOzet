using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.API.Controllers;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using Xunit;

namespace VideoOzet.UnitTests.API.Controllers;

public class ContentRequestsControllerTests
{
    [Fact]
    public async Task ReviseContent_ShouldCreateVersionsAndReturnUpdatedDetail_Synchronously()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dbContext = new AppDbContext(options);
        var requestId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();

        var egitim = new Egitim { Id = egitimId, Ad = "Test Egitim" };
        var request = new ContentRequest
        {
            Id = requestId,
            EgitimId = egitimId,
            Konu = "Genel Konu",
            Durum = ContentRequestDurumu.Tamamlandi,
            GeneratedContent = new GeneratedContent
            {
                Id = Guid.NewGuid(),
                ContentRequestId = requestId,
                ArastirmaOzeti = "Eski Ozet",
                VideoPlani = "Eski Plan",
                LlmModel = "gemini-3.8-flash"
            }
        };

        dbContext.Egitimler.Add(egitim);
        dbContext.ContentRequests.Add(request);
        await dbContext.SaveChangesAsync();

        var mockSynthesis = new Mock<ISynthesisProvider>();
        mockSynthesis.Setup(s => s.ActiveModelName).Returns("gemini-3.8-flash");
        mockSynthesis.Setup(s => s.ReviseContentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Revize Edilmis Metin");

        mockSynthesis.Setup(s => s.ReQualityCheckAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("[{\"Iddia\":\"Iddia 1\",\"Durum\":\"Desteklendi\",\"Aciklama\":\"Tamam\"}]");

        var controller = new ContentRequestDetailsController(dbContext, mockSynthesis.Object);

        var dto = new ReviseContentRequestDto
        {
            RevizeTalimati = "Daha detaylı yap",
            HedefAlan = "hepsi"
        };

        // Act
        var result = await controller.ReviseContent(requestId, dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.Value.Should().NotBeNull();

        var versions = await dbContext.ContentVersions.Where(v => v.ContentRequestId == requestId).ToListAsync();
        versions.Should().HaveCount(2); // V1 (initial) + V2 (revised)
        
        var v1 = versions.First(v => v.VersiyonNo == 1);
        v1.ArastirmaOzeti.Should().Be("Eski Ozet");
        v1.VideoPlani.Should().Be("Eski Plan");

        var v2 = versions.First(v => v.VersiyonNo == 2);
        v2.RevizeTalimati.Should().Be("Daha detaylı yap");
        v2.ArastirmaOzeti.Should().Be("Revize Edilmis Metin");
        v2.VideoPlani.Should().Be("Revize Edilmis Metin");
    }

    [Fact]
    public async Task CreateContentRequest_ShouldSetModToKlasik_AndPublishEvent()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new AppDbContext(options);
        var egitimId = Guid.NewGuid();
        dbContext.Egitimler.Add(new Egitim { Id = egitimId, Ad = "Test Egitim" });
        await dbContext.SaveChangesAsync();

        var mockPublish = new Mock<MassTransit.IPublishEndpoint>();
        var controller = new ContentRequestsController(dbContext, mockPublish.Object);

        var dto = new CreateContentRequestDto
        {
            Konu = "Clean Code",
            HedefUzunluk = "orta",
            HedefKitle = "genel"
        };

        // Act
        var result = await controller.CreateContentRequest(egitimId, dto);

        // Assert
        result.Should().BeOfType<AcceptedResult>();
        var created = await dbContext.ContentRequests.FirstOrDefaultAsync(r => r.EgitimId == egitimId);
        created.Should().NotBeNull();
        created!.Mod.Should().Be(IcerikModu.Klasik);
        created.Konu.Should().Be("Clean Code");

        mockPublish.Verify(p => p.Publish(It.Is<ContentRequestedEvent>(e => e.EgitimId == egitimId && e.Konu == "Clean Code"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRequests_ShouldIncludeModAndVideoSayisi_ForBothModes()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new AppDbContext(options);
        var egitimId = Guid.NewGuid();
        dbContext.Egitimler.Add(new Egitim { Id = egitimId, Ad = "Test Egitim" });

        var classicReq = new ContentRequest
        {
            Id = Guid.NewGuid(),
            EgitimId = egitimId,
            Konu = "Tekil Video",
            Mod = IcerikModu.Klasik,
            Durum = ContentRequestDurumu.Tamamlandi,
            OlusturmaTarihi = DateTime.UtcNow.AddMinutes(-10)
        };

        var plannerReq = new ContentRequest
        {
            Id = Guid.NewGuid(),
            EgitimId = egitimId,
            Konu = "Çoklu Seri",
            Mod = IcerikModu.Planli,
            Durum = ContentRequestDurumu.Tamamlandi,
            OlusturmaTarihi = DateTime.UtcNow
        };

        var plan = new SeriPlani
        {
            Id = Guid.NewGuid(),
            ContentRequestId = plannerReq.Id,
            PlanNo = 1,
            VideoSayisi = 4,
            Durum = SeriPlanDurumu.Onaylandi
        };
        plannerReq.SeriPlanlari.Add(plan);

        dbContext.ContentRequests.AddRange(classicReq, plannerReq);
        dbContext.SeriPlanlari.Add(plan);
        await dbContext.SaveChangesAsync();

        var mockPublish = new Mock<MassTransit.IPublishEndpoint>();
        var controller = new ContentRequestsController(dbContext, mockPublish.Object);

        // Act
        var result = await controller.GetRequests(egitimId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var list = okResult.Value as IEnumerable<object>;
        list.Should().NotBeNull();
        list.Should().HaveCount(2);

        // Verify JSON properties
        var json = System.Text.Json.JsonSerializer.Serialize(list);
        json.Should().Contain("\"Mod\":0");
        json.Should().Contain("\"ModAdi\":\"Klasik\"");
        json.Should().Contain("\"Mod\":1");
        json.Should().Contain("\"ModAdi\":\"Planli\"");
        json.Should().Contain("\"VideoSayisi\":4");
    }
}
