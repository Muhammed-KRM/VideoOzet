using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Events;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Infrastructure.Media;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Mappings;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Data.Repositories;
using Xunit;

namespace VideoOzet.UnitTests.Business.Services;

public class VideoManagerRemoteImportTests
{
    [Fact]
    public async Task ImportRemoteVideosAsync_ShouldSkipDuplicatesAndPublishEventsForNewOnes()
    {
        var egitimId = Guid.NewGuid();
        var egitim = new Egitim { Id = egitimId, ToplamVideoSayisi = 1 };

        var existingVideos = new List<Video>
        {
            new Video { Id = Guid.NewGuid(), EgitimId = egitimId, DisKaynakId = "dQw4w9WgXcQ", Sira = 1 }
        };

        var mockVideoRepo = new Mock<IRepository<Video>>();
        var mockEgitimRepo = new Mock<IRepository<Egitim>>();
        var mockTranscriptRepo = new Mock<IRepository<VideoTranscript>>();
        var mockFileService = new Mock<IFileStorageService>();
        var mockPublish = new Mock<IPublishEndpoint>();
        var mockLogger = new Mock<ILogger<VideoManager>>();

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<VideoProfile>(), new LoggerFactory()).CreateMapper();

        mockEgitimRepo.Setup(r => r.GetByIdAsync(egitimId)).ReturnsAsync(egitim);
        mockVideoRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Video, bool>>>())).ReturnsAsync(existingVideos);

        var manager = new VideoManager(
            mockVideoRepo.Object,
            mockEgitimRepo.Object,
            mockTranscriptRepo.Object,
            mockFileService.Object,
            mockPublish.Object,
            mapper,
            mockLogger.Object);

        var request = new ImportRemoteVideosRequestDto
        {
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            BearerToken = "secret-transient-token",
            Items = new List<RemoteImportItemRequestDto>
            {
                new() { KaynakUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ", Baslik = "Duplicate" },
                new() { KaynakUrl = "https://www.youtube.com/watch?v=abcDEF12345", Baslik = "New Video", SureSaniye = 120 }
            }
        };

        var result = await manager.ImportRemoteVideosAsync(egitimId, request);

        result.EklenenVideolar.Should().HaveCount(1);
        result.EklenenVideolar[0].DisKaynakId.Should().Be("abcDEF12345");
        result.AtlananDuplicateIdler.Should().ContainSingle().Which.Should().Be("dQw4w9WgXcQ");
        egitim.ToplamVideoSayisi.Should().Be(2);
        mockPublish.Verify(p => p.Publish(
            It.Is<RemoteVideoRequestedEvent>(e =>
                e.KaynakUrl == "https://www.youtube.com/watch?v=abcDEF12345" &&
                e.AuthHeader == "secret-transient-token"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryVideoAsync_ShouldRepublishRemoteVideoRequestedEvent_WhenDosyaYoluEmptyAndKaynakUrlSet()
    {
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var video = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            DosyaYolu = string.Empty,
            KaynakUrl = "https://www.youtube.com/watch?v=abcDEF12345",
            KaynakPlatform = KaynakPlatform.YouTube,
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            IslemDurumu = VideoIslemDurumu.Hata
        };

        var mockVideoRepo = new Mock<IRepository<Video>>();
        var mockEgitimRepo = new Mock<IRepository<Egitim>>();
        var mockTranscriptRepo = new Mock<IRepository<VideoTranscript>>();
        var mockFileService = new Mock<IFileStorageService>();
        var mockPublish = new Mock<IPublishEndpoint>();
        var mockLogger = new Mock<ILogger<VideoManager>>();
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<VideoProfile>(), new LoggerFactory()).CreateMapper();

        mockVideoRepo.Setup(r => r.GetByIdAsync(videoId)).ReturnsAsync(video);
        mockTranscriptRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<VideoTranscript, bool>>>())).ReturnsAsync(new List<VideoTranscript>());

        var manager = new VideoManager(
            mockVideoRepo.Object,
            mockEgitimRepo.Object,
            mockTranscriptRepo.Object,
            mockFileService.Object,
            mockPublish.Object,
            mapper,
            mockLogger.Object);

        await manager.RetryVideoAsync(videoId);

        video.IslemDurumu.Should().Be(VideoIslemDurumu.Indiriliyor);
        mockPublish.Verify(p => p.Publish(
            It.Is<RemoteVideoRequestedEvent>(e => e.VideoId == videoId && e.KaynakUrl == video.KaynakUrl),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void YtDlpMediaService_ShouldParseJson3AndVttAndDetectDrmAndManifest()
    {
        var json3 = """
        {
          "events": [
            { "tStartMs": 1000, "dDurationMs": 2000, "segs": [ { "utf8": "Merhaba " }, { "utf8": "dünya" } ] },
            { "tStartMs": 1500, "dDurationMs": 500, "segs": [ { "utf8": "\n" } ] },
            { "tStartMs": 3000, "dDurationMs": 2500, "segs": [ { "utf8": "Bugün test yapıyoruz" } ] }
          ]
        }
        """;
        var (text3, segs3) = YtDlpMediaService.ParseJson3(json3);
        text3.Should().Be("Merhaba dünya Bugün test yapıyoruz");
        segs3.Should().Contain("\"start\":1");

        var vtt = """
        WEBVTT

        00:00:01.000 --> 00:00:03.000
        <c>Merhaba dünya</c>

        00:00:03.000 --> 00:00:05.000
        Merhaba dünya
        İkinci satır
        """;
        var (textVtt, _) = YtDlpMediaService.ParseVtt(vtt);
        textVtt.Should().Be("Merhaba dünya İkinci satır");

        var manifestJson = """
        {
          "egitimAdi": "Deneme",
          "videolar": [
            { "baslik": "Bölüm 1", "mediaUrl": "/cdn/b1.mp4", "transcriptUrl": "/cdn/b1.vtt", "sureSaniye": 180 }
          ]
        }
        """;
        var items = YtDlpMediaService.ParseCourseManifestJson(manifestJson, "https://sitem.com/api/export");
        items.Should().ContainSingle();
        items[0].KaynakUrl.Should().Be("https://sitem.com/cdn/b1.mp4");
        items[0].TranscriptUrl.Should().Be("https://sitem.com/cdn/b1.vtt");
        items[0].KaynakPlatform.Should().Be(KaynakPlatform.CustomApiManifest);

        Action drmAction = () => YtDlpMediaService.ThrowIfDrmDetected("ERROR: This video is DRM protected");
        drmAction.Should().Throw<DrmProtectedContentException>();

        Action hlsDrmAction = () => YtDlpMediaService.ThrowIfHlsManifestHasDrm("#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"skd://fairplay\"");
        hlsDrmAction.Should().Throw<DrmProtectedContentException>();
    }

    [Fact]
    public void EnsureSqliteVideolarSchemaUpgraded_ShouldBeIdempotentAndAddMissingColumns()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"videoozet_upgrade_test_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using (var db = new AppDbContext(options))
            {
                db.Database.OpenConnection();
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE videolar (
                        id TEXT NOT NULL PRIMARY KEY,
                        egitim_id TEXT NOT NULL,
                        baslik TEXT NOT NULL,
                        dosya_yolu TEXT NOT NULL,
                        ses_yolu TEXT NULL,
                        sure TEXT NULL,
                        sira INTEGER NOT NULL DEFAULT 0,
                        dosya_boyutu INTEGER NOT NULL DEFAULT 0,
                        islem_durumu INTEGER NOT NULL DEFAULT 0,
                        kullanim_turu INTEGER NOT NULL DEFAULT 0,
                        IndekslemeTuru INTEGER NOT NULL DEFAULT 0,
                        olusturma_tarihi TEXT NOT NULL,
                        islem_tamamlanma_tarihi TEXT NULL
                    );
                """);

                // Run twice to verify idempotency
                global::Program.EnsureSqliteVideolarSchemaUpgraded(db);
                global::Program.EnsureSqliteVideolarSchemaUpgraded(db);

                var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var conn = db.Database.GetDbConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(videolar);";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    cols.Add(reader.GetString(reader.GetOrdinal("name")));
                }

                cols.Should().Contain(new[]
                {
                    "kaynak_platform",
                    "kaynak_url",
                    "dis_kaynak_id",
                    "kanal_adi",
                    "thumbnail_url",
                    "alim_modu",
                    "transkript_kalite_skoru",
                    "transkript_kaynak_notu"
                });
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task ImportRemoteVideosAsync_ShouldEnforceColumnMaxLengthsAndRejectOverlongUrl()
    {
        var egitimId = Guid.NewGuid();
        var egitim = new Egitim { Id = egitimId, ToplamVideoSayisi = 0 };
        var addedVideos = new List<Video>();

        var mockVideoRepo = new Mock<IRepository<Video>>();
        var mockEgitimRepo = new Mock<IRepository<Egitim>>();
        var mockTranscriptRepo = new Mock<IRepository<VideoTranscript>>();
        var mockFileService = new Mock<IFileStorageService>();
        var mockPublish = new Mock<IPublishEndpoint>();
        var mockLogger = new Mock<ILogger<VideoManager>>();
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<VideoProfile>(), new LoggerFactory()).CreateMapper();

        mockEgitimRepo.Setup(r => r.GetByIdAsync(egitimId)).ReturnsAsync(egitim);
        mockVideoRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Video, bool>>>())).ReturnsAsync(new List<Video>());
        mockVideoRepo.Setup(r => r.AddAsync(It.IsAny<Video>()))
            .Callback<Video>(v => addedVideos.Add(v))
            .ReturnsAsync((Video v) => v);

        var manager = new VideoManager(
            mockVideoRepo.Object,
            mockEgitimRepo.Object,
            mockTranscriptRepo.Object,
            mockFileService.Object,
            mockPublish.Object,
            mapper,
            mockLogger.Object);

        var longChannel = new string('K', 400);
        var longThumb = "https://cdn.example.com/" + new string('t', 1200) + ".jpg";
        var longExtId = new string('E', 250);
        var overlongUrl = "https://cdn.example.com/" + new string('u', 2100) + ".mp4";

        var request = new ImportRemoteVideosRequestDto
        {
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            Items = new List<RemoteImportItemRequestDto>
            {
                new()
                {
                    KaynakUrl = "https://cdn.example.com/valid-video.mp4",
                    Baslik = "Valid With Long Metadata",
                    DisKaynakId = longExtId,
                    KanalAdi = longChannel,
                    ThumbnailUrl = longThumb,
                    KaynakPlatform = KaynakPlatform.DirectUrl
                },
                new()
                {
                    KaynakUrl = overlongUrl,
                    Baslik = "Too Long Url"
                }
            }
        };

        var result = await manager.ImportRemoteVideosAsync(egitimId, request);

        result.EklenenVideolar.Should().ContainSingle();
        result.Hatalar.Should().ContainSingle().Which.Should().Contain("2000");

        addedVideos.Should().ContainSingle();
        var persisted = addedVideos[0];
        persisted.KanalAdi!.Length.Should().Be(300);
        persisted.ThumbnailUrl!.Length.Should().Be(1000);
        persisted.DisKaynakId!.Length.Should().Be(64); // SHA-256 hex
        persisted.KaynakUrl!.Length.Should().BeLessThanOrEqualTo(2000);
    }

    [Fact]
    public async Task ImportRemoteVideosAsync_SpoofedYouTubeHost_ShouldTreatAsDirectUrlAndNotCollideWithYouTubeId()
    {
        var egitimId = Guid.NewGuid();
        var egitim = new Egitim { Id = egitimId, ToplamVideoSayisi = 1 };
        var existingVideos = new List<Video>
        {
            new Video { Id = Guid.NewGuid(), EgitimId = egitimId, DisKaynakId = "dQw4w9WgXcQ", Sira = 1 }
        };
        var addedVideos = new List<Video>();

        var mockVideoRepo = new Mock<IRepository<Video>>();
        var mockEgitimRepo = new Mock<IRepository<Egitim>>();
        var mockTranscriptRepo = new Mock<IRepository<VideoTranscript>>();
        var mockFileService = new Mock<IFileStorageService>();
        var mockPublish = new Mock<IPublishEndpoint>();
        var mockLogger = new Mock<ILogger<VideoManager>>();
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<VideoProfile>(), new LoggerFactory()).CreateMapper();

        mockEgitimRepo.Setup(r => r.GetByIdAsync(egitimId)).ReturnsAsync(egitim);
        mockVideoRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Video, bool>>>())).ReturnsAsync(existingVideos);
        mockVideoRepo.Setup(r => r.AddAsync(It.IsAny<Video>()))
            .Callback<Video>(v => addedVideos.Add(v))
            .ReturnsAsync((Video v) => v);

        var manager = new VideoManager(
            mockVideoRepo.Object,
            mockEgitimRepo.Object,
            mockTranscriptRepo.Object,
            mockFileService.Object,
            mockPublish.Object,
            mapper,
            mockLogger.Object);

        const string spoofedUrl = "https://youtube.com.attacker.com/watch?v=dQw4w9WgXcQ";
        const string spoofedMp4Url = "https://notyoutu.be/dQw4w9WgXcQ.mp4";
        var request = new ImportRemoteVideosRequestDto
        {
            AlimModu = UzaktanAlimModu.AkilliTranskriptOncelikli,
            Items = new List<RemoteImportItemRequestDto>
            {
                new() { KaynakUrl = spoofedUrl, Baslik = "Spoofed Host AutoDetect", KaynakPlatform = KaynakPlatform.Yerel },
                new() { KaynakUrl = spoofedMp4Url, Baslik = "Spoofed Host DefaultPlatform" }
            }
        };

        var result = await manager.ImportRemoteVideosAsync(egitimId, request);

        result.EklenenVideolar.Should().HaveCount(2);
        result.AtlananDuplicateIdler.Should().BeEmpty();
        addedVideos.Should().HaveCount(2);
        addedVideos[0].KaynakPlatform.Should().Be(KaynakPlatform.DirectUrl);
        addedVideos[0].DisKaynakId.Should().Be(spoofedUrl);
        addedVideos[1].DisKaynakId.Should().Be(spoofedMp4Url);
    }
}
