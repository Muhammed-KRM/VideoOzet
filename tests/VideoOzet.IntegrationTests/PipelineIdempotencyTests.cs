using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using Xunit;

namespace VideoOzet.IntegrationTests;

public class PipelineIdempotencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PipelineIdempotencyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Layer1_RedisLock_ShouldPreventConcurrentExecution()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();

        var videoId = Guid.NewGuid();
        var lockKey = $"lock:stt:{videoId}";

        // Act: İlk mesaj kilidi alır
        var firstAttempt = await cacheService.SetIfNotExistsAsync(lockKey, "1", TimeSpan.FromHours(1));

        // İkinci (tekrar eden veya eşzamanlı) mesaj kilidi alamaz
        var secondAttempt = await cacheService.SetIfNotExistsAsync(lockKey, "1", TimeSpan.FromHours(1));

        // Assert
        Assert.True(firstAttempt, "İlk mesaj Redis kilidini başarıyla almalıdır.");
        Assert.False(secondAttempt, "İkinci mesaj Redis kilidini alamamalı ve işlem atlanmalıdır.");
    }

    [Fact]
    public async Task Layer2_DatabaseUniqueConstraint_ShouldPreventDuplicateTranscriptRecords()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var videoId = Guid.NewGuid();
        var egitim = new Egitim
        {
            Id = Guid.NewGuid(),
            Ad = $"Eğitim {Guid.NewGuid():N}",
            Durum = EgitimDurumu.Taslak,
            OlusturmaTarihi = DateTime.UtcNow
        };
        dbContext.Egitimler.Add(egitim);

        var video = new Video
        {
            Id = videoId,
            EgitimId = egitim.Id,
            Baslik = "test.mp4",
            DosyaYolu = "videos/test.mp4",
            DosyaBoyutu = 1024,
            OlusturmaTarihi = DateTime.UtcNow,
            IslemDurumu = VideoIslemDurumu.Bekliyor
        };
        dbContext.Videolar.Add(video);

        var transcript1 = new VideoTranscript
        {
            Id = Guid.NewGuid(),
            VideoId = videoId,
            HamMetin = "İlk transkript metni",
            KelimeSayisi = 3,
            SttModel = "whisper-1",
            OlusturmaTarihi = DateTime.UtcNow
        };
        dbContext.VideoTranscripts.Add(transcript1);
        await dbContext.SaveChangesAsync();

        // Act & Assert: İkinci kez aynı VideoId ile transkript eklemeye çalışmak DB seviyesinde hata vermelidir
        using var scope2 = _factory.Services.CreateScope();
        var dbContext2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        var transcript2 = new VideoTranscript
        {
            Id = Guid.NewGuid(),
            VideoId = videoId, // Aynı VideoId!
            HamMetin = "Mükerrer transkript metni",
            KelimeSayisi = 3,
            SttModel = "whisper-1",
            OlusturmaTarihi = DateTime.UtcNow
        };
        dbContext2.VideoTranscripts.Add(transcript2);

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await dbContext2.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Layer2_DatabaseUniqueConstraint_ShouldPreventDuplicateSummaryRecords()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var videoId = Guid.NewGuid();
        var egitim = new Egitim
        {
            Id = Guid.NewGuid(),
            Ad = $"Eğitim Summary {Guid.NewGuid():N}",
            Durum = EgitimDurumu.Taslak,
            OlusturmaTarihi = DateTime.UtcNow
        };
        dbContext.Egitimler.Add(egitim);

        var video = new Video
        {
            Id = videoId,
            EgitimId = egitim.Id,
            Baslik = "summary_test.mp4",
            DosyaYolu = "videos/summary_test.mp4",
            DosyaBoyutu = 1024,
            OlusturmaTarihi = DateTime.UtcNow,
            IslemDurumu = VideoIslemDurumu.Bekliyor
        };
        dbContext.Videolar.Add(video);

        var summary1 = new VideoSummary
        {
            Id = Guid.NewGuid(),
            VideoId = videoId,
            OzetMetni = "İlk özet metni",
            KonuBasliklari = "[]",
            KonuEtiketleri = "[]",
            LlmModel = "gemini-2.0-flash",
            OlusturmaTarihi = DateTime.UtcNow
        };
        dbContext.VideoSummaries.Add(summary1);
        await dbContext.SaveChangesAsync();

        // Act & Assert: İkinci kez aynı VideoId ile özet ekleme UNIQUE hatası vermelidir
        using var scope2 = _factory.Services.CreateScope();
        var dbContext2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        var summary2 = new VideoSummary
        {
            Id = Guid.NewGuid(),
            VideoId = videoId, // Aynı VideoId!
            OzetMetni = "Mükerrer özet metni",
            KonuBasliklari = "[]",
            KonuEtiketleri = "[]",
            LlmModel = "gemini-2.0-flash",
            OlusturmaTarihi = DateTime.UtcNow
        };
        dbContext2.VideoSummaries.Add(summary2);

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await dbContext2.SaveChangesAsync();
        });
    }
}
