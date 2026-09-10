using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using Pgvector.EntityFrameworkCore;
using Xunit;

namespace VideoOzet.IntegrationTests;

public class PgVectorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PgVectorIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AppDbContext_CanSaveAndQueryPgvector_UsingEfCore()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Veritabanı tablolarını (ve pgvector eklentisini) oluştur
        await dbContext.Database.MigrateAsync();

        var egitimId = Guid.NewGuid();
        var videoId = Guid.NewGuid();

        var egitim = new Egitim
        {
            Id = egitimId,
            Ad = "PgVector Test Eğitimi",
            Aciklama = "Test",
            OlusturmaTarihi = DateTime.UtcNow
        };

        var video = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "Vector Test Video",
            DosyaYolu = "test",
            Sira = 1,
            IslemDurumu = VideoOzet.Data.Enums.VideoIslemDurumu.Bekliyor,
            OlusturmaTarihi = DateTime.UtcNow
        };

        var chunkId = Guid.NewGuid();
        // create a dummy vector: [1, 2, 3] but the DB uses size 1536 by default. 
        // For testing, let's just make a vector of size 1536 since our entity is Vector(1536).
        var dummyVectorArray = new float[1536];
        dummyVectorArray[0] = 1.0f;
        dummyVectorArray[1] = 0.5f;

        var textChunk = new VideoChunkDocument
        {
            Id = chunkId,
            VideoId = videoId,
            StartTimeMs = 0,
            EndTimeMs = 10000,
            Text = "Bu bir test cümlesidir.",
            Embedding = new Vector(dummyVectorArray),
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Egitimler.Add(egitim);
        dbContext.Videolar.Add(video);
        dbContext.VideoChunkDocuments.Add(textChunk);

        await dbContext.SaveChangesAsync();

        // Act
        // Query to find nearest neighbors using pgvector cosine distance:
        var queryVectorArray = new float[1536];
        queryVectorArray[0] = 0.9f;
        queryVectorArray[1] = 0.4f;
        var queryVector = new Vector(queryVectorArray);

        var nearestChunks = await dbContext.VideoChunkDocuments
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(5)
            .ToListAsync();

        // Assert
        nearestChunks.Should().NotBeNull();
        nearestChunks.Should().HaveCountGreaterThan(0);
        nearestChunks.First().Id.Should().Be(chunkId);
    }
}
