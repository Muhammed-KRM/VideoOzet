using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgvector;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using Xunit;

namespace VideoOzet.UnitTests.Services;

public class SourceContextQuotaTests
{
    private static AppDbContext CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static Vector CreateDirectionalVector(int dimension, int primaryIndex, float primaryWeight = 0.9f)
    {
        var arr = Enumerable.Repeat(0.02f, dimension).ToArray();
        arr[Math.Abs(primaryIndex) % dimension] = primaryWeight;
        return new Vector(arr);
    }

    [Fact]
    public async Task BuildContextAsync_On45SourceCourseWithPreferredSourceIds_ShouldGuarantee100PercentAssignedRecall_AndExceed24Chunks()
    {
        var egitimId = Guid.NewGuid();
        using var db = CreateInMemoryDb($"quota-45-sources-{Guid.NewGuid()}");

        var allSourceIds = new List<Guid>();
        // 30 Doküman + 15 Video = 45 Kaynaklı büyük eğitim havuzu
        for (int s = 0; s < 45; s++)
        {
            bool isVideo = s >= 30;
            var sourceId = Guid.NewGuid();
            allSourceIds.Add(sourceId);

            // İlk 25 kaynak sorgu kelimeleriyle çok yoğun eşleşsin, son 20 kaynak ise daha az ortak kelime taşısın
            // (eski 24-chunk sisteminde son kaynaklar tamamen dışarıda kalıyordu!)
            bool highLexicalOverlap = s < 25;
            var title = isVideo
                ? $"Ders_{s + 1:D2}_Algoritmik_Yonetim.mp4"
                : $"Kaynak_{s + 1:D2}_Felsefe_Ve_Irade.pdf";

            if (isVideo)
            {
                db.Videolar.Add(new Video
                {
                    Id = sourceId,
                    EgitimId = egitimId,
                    Baslik = title,
                    IslemDurumu = VideoIslemDurumu.Tamamlandi,
                    KullanimTuru = s % 3 == 0 ? KaynakKullanimTuru.SadeceArgumanVeMantik : KaynakKullanimTuru.DogrudanReferans,
                    Transcript = new VideoTranscript
                    {
                        Id = Guid.NewGuid(),
                        VideoId = sourceId,
                        Dil = "tr",
                        HamMetin = $"Video {s + 1} transkript içeriği. " + new string('v', 3500)
                    }
                });
            }
            else
            {
                db.Dokumanlar.Add(new Dokuman
                {
                    Id = sourceId,
                    EgitimId = egitimId,
                    DosyaAdi = title,
                    IslemDurumu = VideoIslemDurumu.Tamamlandi,
                    KullanimTuru = s % 2 == 0 ? KaynakKullanimTuru.DogrudanReferans : KaynakKullanimTuru.SadeceArgumanVeMantik,
                    DokumanMetin = new DokumanMetin
                    {
                        Id = Guid.NewGuid(),
                        DokumanId = sourceId,
                        OrijinalDil = s % 4 == 0 ? "en" : "tr",
                        HamMetin = $"Doküman {s + 1} tam metin içeriği. " + new string('d', 4000)
                    }
                });
            }

            for (int cIdx = 0; cIdx < 3; cIdx++)
            {
                var body = highLexicalOverlap
                    ? $"Özgür irade, determinizm, Gazzali ve Sapolsky nörobiyoloji argümanları kaynak {s + 1} parça {cIdx + 1}. " + new string('x', 280)
                    : $"Dijital etik, kurumsal özerklik ve bilişsel dikkat mimarisi vaka incelemesi kaynak {s + 1} parça {cIdx + 1}. " + new string('y', 280);

                db.VideoChunkDocuments.Add(new VideoChunkDocument
                {
                    Id = Guid.NewGuid(),
                    EgitimId = egitimId,
                    VideoId = isVideo ? sourceId : null,
                    DokumanId = isVideo ? null : sourceId,
                    ChunkIndex = cIdx,
                    ChunkTipi = ChunkTipi.HamMetin,
                    KaynakBaslik = title,
                    BolumBasligi = $"Alt Bölüm {cIdx + 1}",
                    KaynakDil = (!isVideo && s % 4 == 0) ? "en" : "tr",
                    StartTimeMs = isVideo ? cIdx * 60000 : 0,
                    EndTimeMs = isVideo ? (cIdx + 1) * 60000 : 0,
                    KullanimTuru = KaynakKullanimTuru.DogrudanReferans,
                    Text = body,
                    Embedding = CreateDirectionalVector(64, highLexicalOverlap ? 0 : (s + cIdx + 5))
                });
            }
        }

        await db.SaveChangesAsync();

        // Bölüme atanan 14 kaynak: hem yüksek skorlu hem de düşük sözcüksel/vektörel skorlu (indeks 30..44) kaynakları içeriyor
        var preferredSourceIds = new List<Guid>
        {
            allSourceIds[0],
            allSourceIds[4],
            allSourceIds[9],
            allSourceIds[14],
            allSourceIds[20],
            allSourceIds[26],
            allSourceIds[28],
            allSourceIds[31],
            allSourceIds[34],
            allSourceIds[37],
            allSourceIds[39],
            allSourceIds[41],
            allSourceIds[43],
            allSourceIds[44]
        };

        var queryVec = CreateDirectionalVector(64, 0, 0.95f).ToArray();
        var embedMock = new Mock<IEmbeddingProvider>();
        embedMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
            .ReturnsAsync((List<string> texts, string _) => texts.Select(_ => queryVec).ToList());

        var builder = new SourceContextBuilder(
            db,
            embedMock.Object,
            NullLogger<SourceContextBuilder>.Instance,
            new QueryDecomposer());

        var result = await builder.BuildContextAsync(
            egitimId,
            queries: new[]
            {
                "Özgür irade ve determinizm tartışması",
                "Gazzali kesb kuramı ve Sapolsky nörobiyoloji bulguları",
                "Dijital dikkat mimarisi"
            },
            maxChars: 130000,
            preferredSourceIds: preferredSourceIds);

        // 1. Dinamik parça hedefi sabit 24 sınırını aşmalı ve 30..64 aralığında olmalı
        result.UsedChunkIds.Count.Should().BeGreaterThan(24);
        result.UsedChunkIds.Count.Should().BeInRange(30, 64);

        // 2. Recall@AssignedSource == %100: Atanan 14 kaynağın TAMAMINDAN en az 1 parça seçilmiş olmalı!
        var selectedSourceIds = result.Evidences
            .Select(e => e.DokumanId ?? e.VideoId ?? Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        foreach (var assignedId in preferredSourceIds)
        {
            selectedSourceIds.Should().Contain(
                assignedId,
                $"bölüme atanan kaynak ({assignedId}) taban kota garantisiyle bağlama mutlaka girmelidir");
        }

        double recallAtAssigned = (double)preferredSourceIds.Count(id => selectedSourceIds.Contains(id)) / preferredSourceIds.Count;
        recallAtAssigned.Should().Be(1.0);
    }

    [Fact]
    public async Task BuildContextAsync_ShouldStripStaleEmbeddedModTags_AndEmitLiveKullanimTuruInTopHeader()
    {
        var egitimId = Guid.NewGuid();
        using var db = CreateInMemoryDb($"live-mod-sync-{Guid.NewGuid()}");

        var docId = Guid.NewGuid();
        var videoId = Guid.NewGuid();

        // Kaynak ilk yüklendiğinde DOĞRUDAN REFERANS iken sonradan canlı olarak DIŞ KAYNAK (SadeceArgumanVeMantik) yapılmış
        db.Dokumanlar.Add(new Dokuman
        {
            Id = docId,
            EgitimId = egitimId,
            DosyaAdi = "Sapolsky_Determined.pdf",
            IslemDurumu = VideoIslemDurumu.Tamamlandi,
            KullanimTuru = KaynakKullanimTuru.SadeceArgumanVeMantik,
            DokumanMetin = new DokumanMetin
            {
                Id = Guid.NewGuid(),
                DokumanId = docId,
                OrijinalDil = "en",
                HamMetin = new string('s', 45000)
            }
        });

        // Video ilk yüklendiğinde DIŞ KAYNAK iken sonradan canlı olarak DOĞRUDAN REFERANS yapılmış
        db.Videolar.Add(new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "Gazzali_Kesb_Dersi.mp4",
            IslemDurumu = VideoIslemDurumu.Tamamlandi,
            KullanimTuru = KaynakKullanimTuru.DogrudanReferans,
            Transcript = new VideoTranscript
            {
                Id = Guid.NewGuid(),
                VideoId = videoId,
                Dil = "tr",
                HamMetin = new string('g', 45000)
            }
        });

        var docChunkId = Guid.NewGuid();
        var videoChunkId = Guid.NewGuid();
        var vec = CreateDirectionalVector(32, 1);

        // Chunk metinlerinin içinde eski indekslemeden kalma çelişkili "| Mod: ..." ve "[Mod: ...]" kalıntıları bulunuyor
        db.VideoChunkDocuments.AddRange(
            new VideoChunkDocument
            {
                Id = docChunkId,
                EgitimId = egitimId,
                DokumanId = docId,
                ChunkIndex = 0,
                ChunkTipi = ChunkTipi.HamMetin,
                KaynakBaslik = "Sapolsky_Determined.pdf",
                BolumBasligi = "Bölüm 2",
                KaynakDil = "en",
                KullanimTuru = KaynakKullanimTuru.DogrudanReferans, // eski sütun değeri kalsa bile canlı Dokuman.KullanimTuru kazanmalı!
                Text = "[Kaynak: Sapolsky_Determined.pdf | Tür: Makale/PDF | Bölüm 2 | Dil: EN | Mod: DOĞRUDAN REFERANS]\nPrefrontal korteks ve dopamin salınımı davranışsal tepkileri belirler.",
                Embedding = vec
            },
            new VideoChunkDocument
            {
                Id = videoChunkId,
                EgitimId = egitimId,
                VideoId = videoId,
                ChunkIndex = 0,
                ChunkTipi = ChunkTipi.Transkript,
                KaynakBaslik = "Gazzali_Kesb_Dersi.mp4",
                BolumBasligi = "02:00–05:00",
                KaynakDil = "tr",
                StartTimeMs = 120000,
                EndTimeMs = 300000,
                KullanimTuru = KaynakKullanimTuru.SadeceArgumanVeMantik, // eski sütun değeri
                Text = "[Mod: DIŞ KAYNAK - KOPYALAMAK YASAKTIR] [Kaynak: Gazzali_Kesb_Dersi.mp4 | Tür: Video | 02:00–05:00 | Dil: TR | Mod: DIŞ KAYNAK]\nİnsanın cüzî iradesi ilahi ilim ve kudret dairesinde kesb ile gerçekleşir.",
                Embedding = vec
            });

        await db.SaveChangesAsync();

        var embedMock = new Mock<IEmbeddingProvider>();
        embedMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
            .ReturnsAsync((List<string> texts, string _) => texts.Select(_ => vec.ToArray()).ToList());

        var builder = new SourceContextBuilder(
            db,
            embedMock.Object,
            NullLogger<SourceContextBuilder>.Instance,
            new QueryDecomposer());

        var ctx = await builder.BuildContextAsync(
            egitimId,
            queries: new[] { "dopamin prefrontal korteks Gazzali kesb iradesi" },
            maxChars: 90000,
            preferredSourceIds: new[] { docId, videoId });

        ctx.Evidences.Should().HaveCount(2);

        // 1. Gövde içindeki eski gömülü "| Mod: DOĞRUDAN REFERANS]" ve "[Mod: DIŞ KAYNAK...]" etiketleri tamamen temizlenmiş olmalı
        ctx.ContextText.Should().NotContain("| Mod: DOĞRUDAN REFERANS]");
        ctx.ContextText.Should().NotContain("| Mod: DIŞ KAYNAK]");
        ctx.ContextText.Should().NotContain("[Mod: DIŞ KAYNAK - KOPYALAMAK YASAKTIR]");

        // 2. Üst başlıklarda veritabanındaki CANLI KullanimTuru değerleri basılmalı
        var docEv = ctx.Evidences.Single(e => e.DokumanId == docId);
        docEv.KullanimTuru.Should().Be(KaynakKullanimTuru.SadeceArgumanVeMantik);
        ctx.ContextText.Should().Contain($"DOKÜMAN: Sapolsky_Determined.pdf | Konum: Bölüm 2 | Dil: EN | Mod: DIŞ KAYNAK - KOPYALAMAK KESİNLİKLE YASAKTIR ===");

        var vidEv = ctx.Evidences.Single(e => e.VideoId == videoId);
        vidEv.KullanimTuru.Should().Be(KaynakKullanimTuru.DogrudanReferans);
        ctx.ContextText.Should().Contain($"VİDEO: Gazzali_Kesb_Dersi.mp4 | Konum: 02:00–05:00 | Dil: TR | Mod: DOĞRUDAN REFERANS (Prompter Dahil) ===");
    }
}
