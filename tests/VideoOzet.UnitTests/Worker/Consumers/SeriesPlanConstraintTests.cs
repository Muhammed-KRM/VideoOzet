using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Consumers;
using Xunit;

namespace VideoOzet.UnitTests.Worker.Consumers;

public class SeriesPlanConstraintTests
{
    private static (AppDbContext Db, Guid RequestId, Guid EgitimId) CreateSeededDb(
        string konu = "Algoritmalar Çağında Özgür İrade ve İnsan Özerkliği",
        string ekTonTalimati = "Felsefi derinliği koruyan, izleyiciyi sarsıcı sorularla düşündüren stüdyo üslubu")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"series-plan-constraints-{Guid.NewGuid()}")
            .Options;
        var db = new AppDbContext(options);

        var egitimId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        db.Egitimler.Add(new Egitim { Id = egitimId, Ad = "Özgür İrade ve Dijital Etik" });
        db.ContentRequests.Add(new ContentRequest
        {
            Id = requestId,
            EgitimId = egitimId,
            Konu = konu,
            HedefKitle = "Üniversite öğrencileri ve düşünce meraklıları",
            EkTonTalimati = ekTonTalimati,
            Mod = IcerikModu.Planli,
            Durum = ContentRequestDurumu.Bekliyor
        });
        db.KonuAnalizleri.Add(new KonuAnalizi
        {
            Id = Guid.NewGuid(),
            ContentRequestId = requestId,
            AnaFikir = "Biyolojik belirlenimcilik ile dijital yönlendirme arasında insan iradesinin imkânı.",
            BeklenenKonularJson = JsonSerializer.Serialize(new[]
            {
                "Sapolsky ve Biyolojik Belirlenimcilik",
                "Gazzâlî'de Kesb ve İrade",
                "Dijital Dikkat Ekonomisi",
                "Kuantum Spekülasyonları"
            }),
            KonuHaritasiJson = "[]",
            KaynaktaOlmayanlarJson = JsonSerializer.Serialize(new[]
            {
                "Yazar Tezi Olarak İşlenecek: Tefekkür Boşluğu Kavramı"
            }),
            OnerilenVideoSayisi = 4,
            OneriSuresiDk = 12,
            Durum = AnalizDurumu.Tamamlandi
        });

        db.SaveChanges();
        db.ChangeTracker.Clear();
        return (db, requestId, egitimId);
    }

    private static (Mock<ConsumeContext<SeriesPlanRequestedEvent>> Context, List<object> Published) CreateConsumeContext(
        Guid requestId,
        Guid egitimId)
    {
        var published = new List<object>();
        var ctx = new Mock<ConsumeContext<SeriesPlanRequestedEvent>>();
        ctx.Setup(c => c.Message).Returns(new SeriesPlanRequestedEvent
        {
            ContentRequestId = requestId,
            EgitimId = egitimId
        });
        ctx.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        ctx.Setup(c => c.Publish(It.IsAny<PipelineProgressEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineProgressEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);
        ctx.Setup(c => c.Publish(It.IsAny<SeriesPlanGeneratedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SeriesPlanGeneratedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);
        ctx.Setup(c => c.Publish(It.IsAny<ContentErrorEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ContentErrorEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        return (ctx, published);
    }

    [Fact]
    public async Task Consume_WithVideoSayisi2AndSure20_ShouldPassDiffAwarePreviousPlan_EnforceMergeAndGenericTitleFix_AndPersistRichFields()
    {
        var (db, requestId, egitimId) = CreateSeededDb();

        var source1Id = Guid.NewGuid();
        var source2Id = Guid.NewGuid();
        var source3Id = Guid.NewGuid();

        // Mevcut onaylanmamış taslak plan (Diff-Aware revizyon için önceki plan olarak LLM'e aktarılmalı!)
        var existingPlan = new SeriPlani
        {
            Id = Guid.NewGuid(),
            ContentRequestId = requestId,
            PlanNo = 1,
            VideoSayisi = 3,
            VarsayilanVideoSuresiDk = 10,
            Durum = SeriPlanDurumu.Olusturuluyor,
            Onaylandi = false,
            KullaniciKisitlariJson = JsonSerializer.Serialize(new SeriesDraftDto
            {
                VideoSayisi = 2,
                VarsayilanSureDk = 20,
                ZorunluKonular = new List<string> { "Gazzâlî'de Kesb ve İrade" },
                HaricKonular = new List<string> { "Kuantum Spekülasyonları" },
                RevizyonTalimati = "1. videoyu mevcut kurguyu koruyarak derinleştir, toplam 2 video ve 20 dakika olsun."
            })
        };
        db.SeriPlanlari.Add(existingPlan);
        db.SeriBolumler.AddRange(
            new SeriBolum
            {
                Id = Guid.NewGuid(),
                SeriPlaniId = existingPlan.Id,
                BolumNo = 1,
                CalismaBasligi = "Önceki Taslak 1: Biyolojik Zincirler ve Sapolsky",
                Kanca = "Kararlarımız bizden milisaniyeler önce mi veriliyor?",
                HedefSureDk = 10,
                AnaFikir = "Önceki taslağın birinci bölüm ana fikri.",
                KonularJson = "[\"Sapolsky nörobiyoloji\"]"
            },
            new SeriBolum
            {
                Id = Guid.NewGuid(),
                SeriPlaniId = existingPlan.Id,
                BolumNo = 2,
                CalismaBasligi = "Önceki Taslak 2: Klasik Kelamda İrade",
                HedefSureDk = 10,
                AnaFikir = "Önceki taslağın ikinci bölüm ana fikri.",
                KonularJson = "[\"Klasik tartışmalar\"]"
            },
            new SeriBolum
            {
                Id = Guid.NewGuid(),
                SeriPlaniId = existingPlan.Id,
                BolumNo = 3,
                CalismaBasligi = "Önceki Taslak 3: Dijital Algoritmalar",
                HedefSureDk = 10,
                AnaFikir = "Önceki taslağın üçüncü bölüm ana fikri.",
                KonularJson = "[\"Öneri sistemleri\"]"
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sourceCards = new List<CourseSourceCardDto>
        {
            new(source1Id, "Dokuman", "Sapolsky_Determined.pdf", "en", "SadeceArgumanVeMantik", 25,
                new List<string> { "Prefrontal korteks", "Dopamin ve belirlenimcilik" }, "Sapolsky'nin biyolojik determinizm tezi"),
            new(source2Id, "Video", "Gazzali_Irade_Semineri.mp4", "tr", "DogrudanReferans", 18,
                new List<string> { "Gazzâlî", "Kesb ve cüzî irade" }, "Gazzâlî'de nedensellik ve irade"),
            new(source3Id, "Dokuman", "Zuboff_Gozetim_Kapitalizmi.pdf", "tr", "SadeceArgumanVeMantik", 22,
                new List<string> { "Algoritmik yönlendirme", "Dikkat ekonomisi" }, "Dijital platformların davranış tahmini")
        };

        var mapperMock = new Mock<ISourceTopicMapper>();
        mapperMock.Setup(m => m.GetStructuredSourceCardsAsync(egitimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceCards);

        var capturedPreviousPlans = new List<string>();
        var capturedConstraints = new List<string>();
        int planCallCount = 0;

        // 1. Çağrıda LLM kısıtları ihlal ediyor: 4 bölüm döndürüyor, "Bölüm 1" jenerik başlığı kullanıyor ve süreyi 8 dk veriyor!
        var invalidFirstResponse = JsonSerializer.Serialize(new
        {
            VideoSayisi = 4,
            VarsayilanVideoSuresiDk = 8,
            OneridenFarkli = true,
            SeriHaritasi = new object[]
            {
                new
                {
                    BolumNo = 1,
                    CalismaBasligi = "Bölüm 1",
                    Kanca = "Beynimiz biz fark etmeden karar veriyor olabilir mi?",
                    HedefSureDk = 8,
                    AnaFikir = "Nörobiyolojik süreçlerin karar alma üzerindeki etkileri incelenir.",
                    Konular = new[] { "Sapolsky'nin prefrontal korteks ve dopamin argümanları", "Kuantum Spekülasyonları ve rastlantısallık" },
                    SahneIskeleti = new[]
                    {
                        new { SahneNo = 1, Baslik = "Libet ve Sapolsky Deneyleri", TahminiSureDk = 4, AciklamaVeArgumanlar = "Nöral ateşleme zinciri", AtananKaynakKodlari = new[] { "Sapolsky_Determined.pdf" } },
                        new { SahneNo = 2, Baslik = "Biyolojik Sınırlar", TahminiSureDk = 4, AciklamaVeArgumanlar = "Hormonlar ve çevre", AtananKaynakKodlari = new[] { "Sapolsky_Determined.pdf" } }
                    },
                    AtananKaynakIdleri = new[] { source1Id }
                },
                new
                {
                    BolumNo = 2,
                    CalismaBasligi = "Video 2",
                    Kanca = "Algoritmalar arzularımızı nasıl yeniden kodluyor?",
                    HedefSureDk = 8,
                    AnaFikir = "Dijital dikkat ekonomisinin insan seçimlerini daraltması tartışılır.",
                    Konular = new[] { "Öneri algoritmaları ve davranışsal öngörü" },
                    SahneIskeleti = new[]
                    {
                        new { SahneNo = 1, Baslik = "Gözetim Kapitalizmi", TahminiSureDk = 8, AciklamaVeArgumanlar = "Davranış fazlası", AtananKaynakKodlari = new[] { "Zuboff_Gozetim_Kapitalizmi.pdf" } }
                    },
                    AtananKaynakIdleri = new[] { source3Id }
                },
                new
                {
                    BolumNo = 3,
                    CalismaBasligi = "Dijital Çağda Tefekkür Boşluğu ve Direniş",
                    Kanca = "Hız çağında durabilmek özgürlüğün son kalesi midir?",
                    HedefSureDk = 8,
                    AnaFikir = "Yazarın özgün tefekkür boşluğu teziyle özerkliğin yeniden inşası.",
                    Konular = new[] { "Tefekkür boşluğu ve bilinçli yavaşlama" },
                    SahneIskeleti = new[]
                    {
                        new { SahneNo = 1, Baslik = "Yazar Tezi Sentezi", TahminiSureDk = 8, AciklamaVeArgumanlar = "İçsel özerklik", AtananKaynakKodlari = new[] { "[YAZAR-TEZİ]" } }
                    },
                    AtananKaynakIdleri = new[] { source2Id }
                },
                new
                {
                    BolumNo = 4,
                    CalismaBasligi = "Etik Sorumluluk ve Hukuki Sonuçlar",
                    Kanca = "Özgür irade yoksa sorumluluk kime aittir?",
                    HedefSureDk = 8,
                    AnaFikir = "Ahlaki ve hukuki sorumluluk kavramlarının yeniden yorumlanması.",
                    Konular = new[] { "Ahlaki fail ve sorumluluk" },
                    SahneIskeleti = new[]
                    {
                        new { SahneNo = 1, Baslik = "Sorumluluk Felsefesi", TahminiSureDk = 8, AciklamaVeArgumanlar = "Kapanış sentezi", AtananKaynakKodlari = new[] { "Gazzali_Irade_Semineri.mp4" } }
                    },
                    AtananKaynakIdleri = new[] { source2Id }
                }
            },
            DisaridaBirakilanlar = new[] { "Kuantum Spekülasyonları kullanıcı kısıtıyla hariç tutuldu." }
        });

        // 2. Çağrıda (düzeltme çağrısı) LLM hâlâ 3 bölüm ve ilkine "Bölüm 1" döndürsün ki C# deterministik birleştirme + başlık türetme devreye girsin!
        var stillImperfectRepairResponse = JsonSerializer.Serialize(new
        {
            VideoSayisi = 3,
            VarsayilanVideoSuresiDk = 12,
            OneridenFarkli = true,
            SeriHaritasi = new object[]
            {
                new
                {
                    BolumNo = 1,
                    CalismaBasligi = "Bölüm 1",
                    Kanca = "Kararlarımız bizden önce nöronlarımızda mı yazılıyor?",
                    HedefSureDk = 12,
                    AnaFikir = "Sapolsky'nin biyolojik belirlenimcilik argümanları ile klasik irade tartışmaları karşılaştırılır.",
                    Konular = new[] { "Nörobiyolojik Belirlenimcilik: Sapolsky ve Prefrontal Korteks Bulguları", "Kuantum Spekülasyonları" },
                    SahneIskeleti = new[]
                    {
                        new { SahneNo = 1, Baslik = "Açılış Paradoksu ve Libet Deneyi", TahminiSureDk = 5, AciklamaVeArgumanlar = "Karar anı analizi", AtananKaynakKodlari = new[] { "Sapolsky_Determined.pdf" } },
                        new { SahneNo = 2, Baslik = "Sapolsky ve Biyolojik Nedensellik", TahminiSureDk = 7, AciklamaVeArgumanlar = "Hormonlar ve geçmiş çevre", AtananKaynakKodlari = new[] { "Sapolsky_Determined.pdf" } }
                    },
                    AtananKaynakIdleri = new[] { source1Id }
                },
                new
                {
                    BolumNo = 2,
                    CalismaBasligi = "Algoritmik Gözetim ve Arzu Mimarisi",
                    Kanca = "Ekranı kaydıran parmak kimin iradesiyle hareket ediyor?",
                    HedefSureDk = 12,
                    AnaFikir = "Dijital platformların dikkat ekonomisiyle seçimleri yönlendirmesi incelenir.",
                    Konular = new[] { "Gözetim kapitalizmi ve öneri algoritmaları" },
                    SahneIskeleti = new[]
                    {
                        new { SahneNo = 1, Baslik = "Dikkat Ekonomisi Mekanizması", TahminiSureDk = 6, AciklamaVeArgumanlar = "Dopamin döngüsü", AtananKaynakKodlari = new[] { "Zuboff_Gozetim_Kapitalizmi.pdf" } },
                        new { SahneNo = 2, Baslik = "Yönlendirilen Tercihler", TahminiSureDk = 6, AciklamaVeArgumanlar = "Algoritmik mimari", AtananKaynakKodlari = new[] { "Zuboff_Gozetim_Kapitalizmi.pdf" } }
                    },
                    AtananKaynakIdleri = new[] { source3Id }
                },
                new
                {
                    BolumNo = 3,
                    CalismaBasligi = "Tefekkür Boşluğu ve Ahlaki Özerkliğin Yeniden İnşası",
                    Kanca = "Dürtü ile eylem arasına bilinçli bir mesafe koyabilir miyiz?",
                    HedefSureDk = 12,
                    AnaFikir = "Yazarın tefekkür boşluğu teziyle iradenin yeniden kazanılması.",
                    Konular = new[] { "Tefekkür boşluğu ve irade eğitimi" },
                    SahneIskeleti = new[]
                    {
                        new { SahneNo = 1, Baslik = "Tefekkür ve Direniş", TahminiSureDk = 12, AciklamaVeArgumanlar = "Yazar tezi kapanışı", AtananKaynakKodlari = new[] { "Gazzali_Irade_Semineri.mp4" } }
                    },
                    AtananKaynakIdleri = new[] { source2Id }
                }
            },
            DisaridaBirakilanlar = new[] { "Kuantum Spekülasyonları" }
        });

        var synthesisMock = new Mock<ISynthesisProvider>();
        synthesisMock.Setup(s => s.GenerateSeriesPlanAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, string, string, CancellationToken>(
                (_, _, _, _, _, constraints, prevPlan, _) =>
                {
                    capturedConstraints.Add(constraints);
                    capturedPreviousPlans.Add(prevPlan);
                })
            .ReturnsAsync(() =>
            {
                planCallCount++;
                return planCallCount == 1 ? invalidFirstResponse : stillImperfectRepairResponse;
            });

        var consumer = new SeriesPlanConsumer(
            db,
            synthesisMock.Object,
            mapperMock.Object,
            new Mock<ILogService>().Object,
            NullLogger<SeriesPlanConsumer>.Instance);

        var (ctx, published) = CreateConsumeContext(requestId, egitimId);
        await consumer.Consume(ctx.Object);

        published.OfType<ContentErrorEvent>().Should().BeEmpty();
        published.OfType<SeriesPlanGeneratedEvent>().Should().ContainSingle();

        // 1. Diff-Aware: Önceki plan silinmeden önce serileştirilip LLM'e aktarılmış olmalı!
        planCallCount.Should().Be(2, "ilk çıktıda bölüm sayısı/jenerik başlık ihlali olduğu için 1 adet LLM düzeltme çağrısı yapılmalı");
        capturedPreviousPlans[0].Should().NotBeNullOrWhiteSpace();
        using (var prevDoc = JsonDocument.Parse(capturedPreviousPlans[0]))
        {
            var prevMap = prevDoc.RootElement.GetProperty("SeriHaritasi");
            prevMap.GetArrayLength().Should().Be(3);
            prevMap[0].GetProperty("CalismaBasligi").GetString()
                .Should().Be("Önceki Taslak 1: Biyolojik Zincirler ve Sapolsky");
        }
        capturedConstraints[0].Should().Contain("2");
        capturedConstraints[0].Should().Contain("20");

        // 2. Veritabanında kesin olarak 2 bölüm ve her bölümde 20 dk süre zorlanmış olmalı!
        db.ChangeTracker.Clear();
        var savedPlan = await db.SeriPlanlari
            .Include(p => p.SeriBolumler)
            .SingleAsync(p => p.ContentRequestId == requestId);

        savedPlan.Durum.Should().Be(SeriPlanDurumu.OnayBekliyor);
        savedPlan.VideoSayisi.Should().Be(2);
        savedPlan.VarsayilanVideoSuresiDk.Should().Be(20);
        savedPlan.OneridenFarkli.Should().BeTrue();

        var episodes = savedPlan.SeriBolumler.OrderBy(b => b.BolumNo).ToList();
        episodes.Should().HaveCount(2);
        episodes.Select(e => e.BolumNo).Should().Equal(1, 2);

        // 3. Jenerik "Bölüm 1" başlığı deterministik olarak anlamlı başlıkla değiştirilmiş olmalı
        foreach (var ep in episodes)
        {
            SeriesPlanConsumer.IsGenericEpisodeTitle(ep.CalismaBasligi).Should().BeFalse();
            ep.CalismaBasligi.Should().NotBe("Bölüm 1");
            ep.HedefSureDk.Should().Be(20);
            ep.Kanca.Should().NotBeNullOrWhiteSpace();

            var scenes = JsonSerializer.Deserialize<List<SeriesSceneOutlineItemDto>>(ep.SahneIskeletiJson);
            scenes.Should().NotBeNullOrEmpty();
            scenes!.Sum(s => s.TahminiSureDk).Should().Be(20);

            var assignedIds = JsonSerializer.Deserialize<List<Guid>>(ep.AtananKaynaklarJson);
            assignedIds.Should().NotBeNullOrEmpty();
        }

        // 4. ZorunluKonular dahil edilmiş, HaricKonular temizlenmiş ve birleştirilen 2.+3. bölümün kaynakları korunmuş olmalı
        var allSavedTopics = episodes
            .SelectMany(e => JsonSerializer.Deserialize<List<string>>(e.KonularJson) ?? new List<string>())
            .ToList();
        allSavedTopics.Should().Contain(t => t.Contains("Gazzâlî'de Kesb ve İrade", StringComparison.OrdinalIgnoreCase));
        allSavedTopics.Should().NotContain(t => t.Contains("Kuantum Spekülasyonları", StringComparison.OrdinalIgnoreCase));

        var allAssignedIds = episodes
            .SelectMany(e => JsonSerializer.Deserialize<List<Guid>>(e.AtananKaynaklarJson) ?? new List<Guid>())
            .ToHashSet();
        allAssignedIds.Should().Contain(new[] { source1Id, source2Id, source3Id });
    }

    [Fact]
    public async Task Consume_WhenLlmReturnsFewerEpisodesThanRequested_ShouldDeterministicallySplitToExactVideoSayisi()
    {
        var (db, requestId, egitimId) = CreateSeededDb();
        var cardId = Guid.NewGuid();

        var draftPlan = new SeriPlani
        {
            Id = Guid.NewGuid(),
            ContentRequestId = requestId,
            PlanNo = 1,
            Durum = SeriPlanDurumu.Olusturuluyor,
            Onaylandi = false,
            KullaniciKisitlariJson = JsonSerializer.Serialize(new SeriesDraftDto
            {
                VideoSayisi = 3,
                VarsayilanSureDk = 20,
                VideoSureleri = new Dictionary<int, int> { [2] = 25 },
                Talimat = "Konuyu 3 ayrı derinlikli videoya böl."
            })
        };
        db.SeriPlanlari.Add(draftPlan);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var mapperMock = new Mock<ISourceTopicMapper>();
        mapperMock.Setup(m => m.GetStructuredSourceCardsAsync(egitimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CourseSourceCardDto>
            {
                new(cardId, "Dokuman", "Ozgur_Irade_Derleme.pdf", "tr", "DogrudanReferans", 30,
                    new List<string> { "Klasik Felsefe", "Nörobilim", "Dijital Etik" }, "Kapsamlı derleme")
            });

        // LLM hem ilk hem onarım çağrısında inatla tek bir dev bölüm döndürsün -> C# deterministik bölme (SplitEpisode) tam 3 bölüme çıkarmalı!
        var singleGiantEpisodeJson = JsonSerializer.Serialize(new
        {
            VideoSayisi = 1,
            VarsayilanVideoSuresiDk = 15,
            OneridenFarkli = true,
            SeriHaritasi = new[]
            {
                new
                {
                    BolumNo = 1,
                    CalismaBasligi = "İrade Tartışmasının Üç Büyük Cephesi",
                    Kanca = "Seçimlerimiz kime aittir?",
                    HedefSureDk = 15,
                    AnaFikir = "Klasik kelam, modern nörobilim ve dijital algoritmalar ekseninde kapsamlı irade analizi.",
                    Konular = new[]
                    {
                        "Klasik Felsefe ve Kelamda İrade Özgürlüğü",
                        "Modern Nörobilim ve Biyolojik Belirlenimcilik",
                        "Dijital Algoritmalar ve Arzu Mimarisi",
                        "Ahlaki Özerklik ve Tefekkür Pratiği"
                    },
                    SahneIskeleti = new[]
                    {
                        new { SahneNo = 1, Baslik = "Klasik Çerçeve", TahminiSureDk = 5, AciklamaVeArgumanlar = "Tarihsel zemin", AtananKaynakKodlari = new[] { "Ozgur_Irade_Derleme.pdf" } },
                        new { SahneNo = 2, Baslik = "Nörobiyolojik Bulgular", TahminiSureDk = 5, AciklamaVeArgumanlar = "Beyin araştırmaları", AtananKaynakKodlari = new[] { "Ozgur_Irade_Derleme.pdf" } },
                        new { SahneNo = 3, Baslik = "Dijital Yönlendirme", TahminiSureDk = 5, AciklamaVeArgumanlar = "Algoritmik etki", AtananKaynakKodlari = new[] { "Ozgur_Irade_Derleme.pdf" } }
                    },
                    AtananKaynakIdleri = new[] { cardId }
                }
            },
            DisaridaBirakilanlar = Array.Empty<string>()
        });

        var synthesisMock = new Mock<ISynthesisProvider>();
        synthesisMock.Setup(s => s.GenerateSeriesPlanAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(singleGiantEpisodeJson);

        var consumer = new SeriesPlanConsumer(
            db,
            synthesisMock.Object,
            mapperMock.Object,
            new Mock<ILogService>().Object,
            NullLogger<SeriesPlanConsumer>.Instance);

        var (ctx, published) = CreateConsumeContext(requestId, egitimId);
        await consumer.Consume(ctx.Object);

        published.OfType<ContentErrorEvent>().Should().BeEmpty();

        db.ChangeTracker.Clear();
        var savedPlan = await db.SeriPlanlari.Include(p => p.SeriBolumler).SingleAsync(p => p.ContentRequestId == requestId);
        savedPlan.VideoSayisi.Should().Be(3);

        var episodes = savedPlan.SeriBolumler.OrderBy(b => b.BolumNo).ToList();
        episodes.Should().HaveCount(3);
        episodes[0].HedefSureDk.Should().Be(20);
        episodes[1].HedefSureDk.Should().Be(25, "VideoSureleri[2]=25 özel kısıtı uygulanmalı");
        episodes[2].HedefSureDk.Should().Be(20);
        episodes.All(e => !SeriesPlanConsumer.IsGenericEpisodeTitle(e.CalismaBasligi)).Should().BeTrue();
    }
}
