using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using Xunit;

namespace VideoOzet.UnitTests.Business.Services;

public class SourceTopicMapperTests
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly Mock<ISynthesisProvider> _mockSynthesis;
    private readonly Mock<ITextChunker> _mockChunker;
    private readonly Mock<ILogger<SourceTopicMapper>> _mockLogger;

    public SourceTopicMapperTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _mockSynthesis = new Mock<ISynthesisProvider>();
        _mockChunker = new Mock<ITextChunker>();
        _mockLogger = new Mock<ILogger<SourceTopicMapper>>();
    }

    [Fact]
    public async Task BuildTopicDigestAsync_WhenNoSources_ReturnsEmptyDigest()
    {
        using var db = new AppDbContext(_dbOptions);
        var egitimId = Guid.NewGuid();

        var mapper = new SourceTopicMapper(db, _mockSynthesis.Object, _mockChunker.Object, _mockLogger.Object);
        var result = await mapper.BuildTopicDigestAsync(egitimId);

        result.Should().BeEmpty();
        _mockSynthesis.Verify(s => s.ExtractSourceTopicsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildTopicDigestAsync_WhenVideoNotCached_CallsSynthesisAndPersistsExtraction()
    {
        using var db = new AppDbContext(_dbOptions);
        var egitimId = Guid.NewGuid();
        var videoId = Guid.NewGuid();

        db.Videolar.Add(new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "Giriş ve Mimari",
            Sira = 1,
            Summary = new VideoSummary
            {
                VideoId = videoId,
                OzetMetni = "Bu videoda mikroservis mimarisinin temelleri anlatılmıştır.",
                KonuBasliklari = "[\"Giriş\", \"Monolit vs Mikroservis\"]"
            }
        });
        await db.SaveChangesAsync();

        var simulatedTopicsJson = "[{\"baslik\":\"Mikroservis Mimarisi\",\"aciklama\":\"Temel prensipler\",\"tahmini_sure_dk\":15}]";
        _mockChunker.Setup(c => c.ChunkText(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(new List<string> { "Bu videoda mikroservis mimarisinin temelleri anlatılmıştır." });

        _mockSynthesis.Setup(s => s.ExtractSourceTopicsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(simulatedTopicsJson);

        var mapper = new SourceTopicMapper(db, _mockSynthesis.Object, _mockChunker.Object, _mockLogger.Object);
        var digest = await mapper.BuildTopicDigestAsync(egitimId);

        digest.Should().Contain("[VİDEO: Giriş ve Mimari");
        digest.Should().Contain("Mikroservis Mimarisi: Temel prensipler (~15 dk)");

        var saved = await db.KaynakKonuCikarimlari.FirstOrDefaultAsync(k => k.KaynakId == videoId);
        saved.Should().NotBeNull();
        saved!.IcerikHash.Should().NotBeNullOrWhiteSpace();
        saved.KonularJson.Should().Contain("Mikroservis Mimarisi");
    }

    [Fact]
    public async Task BuildTopicDigestAsync_WhenCachedWithSameHash_UsesCacheWithoutCallingSynthesis()
    {
        using var db = new AppDbContext(_dbOptions);
        var egitimId = Guid.NewGuid();
        var videoId = Guid.NewGuid();

        var summaryText = "Bu videoda Docker kullanımı anlatılmaktadır.";
        var headersText = "[\"Docker Kurulumu\"]";
        var fullSourceText = summaryText + "\nKonu Başlıkları:\n" + headersText;

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fullSourceText));
        var expectedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        db.Videolar.Add(new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "Docker Temelleri",
            Sira = 1,
            Summary = new VideoSummary
            {
                VideoId = videoId,
                OzetMetni = summaryText,
                KonuBasliklari = headersText
            }
        });

        db.KaynakKonuCikarimlari.Add(new KaynakKonuCikarimi
        {
            KaynakId = videoId,
            KaynakTuru = "Video",
            PromptVersiyonu = 1,
            IcerikHash = expectedHash,
            KonularJson = "[{\"baslik\":\"Önbellekten Gelen Konu\",\"aciklama\":\"Açıklama\",\"tahmini_sure_dk\":10}]"
        });
        await db.SaveChangesAsync();

        var mapper = new SourceTopicMapper(db, _mockSynthesis.Object, _mockChunker.Object, _mockLogger.Object);
        var digest = await mapper.BuildTopicDigestAsync(egitimId);

        digest.Should().Contain("Önbellekten Gelen Konu");
        _mockSynthesis.Verify(s => s.ExtractSourceTopicsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
