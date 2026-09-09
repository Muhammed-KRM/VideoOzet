using System;
using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoOzet.Data;
using VideoOzet.Data.Context;
using VideoOzet.Data.Repositories;
using Xunit;

namespace VideoOzet.UnitTests.Data;

public class DataLayerTests
{
    [Fact]
    public void AddDataLayer_ShouldRegisterDbContextAndRepositories()
    {
        // Arrange
        var services = new ServiceCollection();
        var connStr = "Host=localhost;Database=test_db;Username=test;Password=test";

        // Act
        services.AddDataLayer(connStr);

        // Assert
        services.Any(s => s.ServiceType == typeof(IRepository<>)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(AppDbContext)).Should().BeTrue();
    }

    [Fact]
    public void Entities_ShouldInitializeAndRetainProperties()
    {
        // Arrange & Act
        var egitimId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var egitim = new VideoOzet.Data.Entities.Egitim
        {
            Id = egitimId,
            Ad = "Test Eğitim",
            Aciklama = "Açıklama",
            Durum = VideoOzet.Data.Enums.EgitimDurumu.Taslak,
            OlusturmaTarihi = now
        };

        var video = new VideoOzet.Data.Entities.Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "Ders 1",
            DosyaYolu = "/path/test.mp4",
            IslemDurumu = VideoOzet.Data.Enums.VideoIslemDurumu.Bekliyor,
            OlusturmaTarihi = now
        };

        var transcript = new VideoOzet.Data.Entities.VideoTranscript
        {
            Id = Guid.NewGuid(),
            VideoId = videoId,
            HamMetin = "Transkript metni"
        };

        var summary = new VideoOzet.Data.Entities.VideoSummary
        {
            Id = Guid.NewGuid(),
            VideoId = videoId,
            OzetMetni = "Özet metni"
        };

        // Assert
        egitim.Id.Should().Be(egitimId);
        egitim.Ad.Should().Be("Test Eğitim");
        video.Id.Should().Be(videoId);
        video.Baslik.Should().Be("Ders 1");
        transcript.HamMetin.Should().Be("Transkript metni");
        summary.OzetMetni.Should().Be("Özet metni");
    }
}
