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
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Consumers;
using Xunit;

namespace VideoOzet.UnitTests.Services;

public class VeriScoreQualityCheckTests
{
    private const string AuthorThesisText =
        "Modern algoritmik yönlendirme, klasik dışsal baskıdan farklı olarak bireyin arzu mimarisini içeriden yeniden kurar; " +
        "gerçek ahlaki özerklik, dürtüsel tepki ile eylem arasına bilinçli bir 'tefekkür boşluğu' yerleştirebilme terbiyesidir.";

    private const string GroundedSourceContext = """
        === [K1] | DOKÜMAN: Sapolsky_Determined.pdf | Konum: Bölüm 3 | Dil: EN | Mod: DIŞ KAYNAK - KOPYALAMAK KESİNLİKLE YASAKTIR ===
        Prefrontal korteks karar anından saniyeler önceki nöral uyarılma geçmişinden, stres hormonlarından ve çevresel koşullardan etkilenir.

        === [K2] | VİDEO: Gazzali_Irade_Semineri.mp4 | Konum: 04:10–08:45 | Dil: TR | Mod: DOĞRUDAN REFERANS (Prompter Dahil) ===
        Gazzâlî'ye göre insan fiillerinde bilgi, irade ve kudret mertebeleri birlikte işler; kesb kavramı cebir ile mutlak bağımsızlık arasında ince bir denge kurar.
        """;

    [Fact]
    public async Task EvaluateAsync_WithAuthorThesisContext_ShouldNeverMarkAuthorThesisUnsupported_EvenIfLlmMisflagsIt_AndShouldCatchFabricated37PercentStat()
    {
        var summaryWithThesisAndHallucination =
            "Sapolsky prefrontal korteksin stres hormonları ve önceki çevresel koşullarla şekillendiğini ortaya koyar [K1]. " +
            "Gazzâlî ise bilgi, irade ve kudret mertebeleri üzerinden kesb dengesini açıklar [K2]. " +
            "Yazarın özgün perspektifiyle modern algoritmik yönlendirme bireyin arzu mimarisini içeriden yeniden kurar ve gerçek özerklik dürtü ile eylem arasına bilinçli bir tefekkür boşluğu inşa edebilmektir. " +
            "Ancak yapılan bir nöropazarlama çalışmasına göre dijital kullanıcıların %37'si kararlarını tamamen bilinçdışı bildirim tetikleyicileriyle vermektedir.";

        var prompterPlan = """
            ## Sahne 1: Arzu Mimarisi ve Tefekkür Boşluğu
            **Zaman Aralığı:** 00:00 - 04:00
            ### 1. [Görsel & Edit Yönergesi]
            - Kamera: Yakın plan stüdyo
            ### 2. [Prompter Metni (Aynen Okunacak)]
            > "Algoritmalar bizi dışarıdan zorlamaz; arzu mimarimizi içeriden yeniden kurar. Özgürlük, uyaranla tepki arasına koyduğumuz o tefekkür boşluğunda başlar."
            """;

        string? capturedQcInput = null;
        var synthMock = new Mock<ISynthesisProvider>();
        synthMock.Setup(s => s.BatchQualityCheckAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, int, CancellationToken>((content, _, _, _) => capturedQcInput = content)
            .ReturnsAsync("""
                [
                  {
                    "iddia": "Prefrontal korteks stres hormonları ve çevresel koşullardan etkilenir.",
                    "iddiaTuru": "olgusal_kaynak_iddiasi",
                    "durum": "desteklendi",
                    "kanitKodu": "[K1]",
                    "kaynakAlani": "ArastirmaOzeti",
                    "aciklama": "[K1] kaynağında açıkça belirtilmiştir."
                  },
                  {
                    "iddia": "Modern algoritmik yönlendirme bireyin arzu mimarisini içeriden yeniden kurar ve gerçek özerklik dürtü ile eylem arasına tefekkür boşluğu inşa edebilmektir.",
                    "iddiaTuru": "yazar_tezi_yorumu",
                    "durum": "desteklenmedi",
                    "kanitKodu": "-",
                    "kaynakAlani": "VideoPlani",
                    "aciklama": "Bu felsefi yorum [K1] ve [K2] kaynaklarında doğrudan yer almamaktadır."
                  }
                ]
                """);

        var qcService = new QualityCheckService(
            synthMock.Object,
            NullLogger<QualityCheckService>.Instance,
            new DeterministicClaimScanner());

        var qcResult = await qcService.EvaluateAsync(
            summaryText: summaryWithThesisAndHallucination,
            sourceContext: GroundedSourceContext,
            authorThesisContext: AuthorThesisText,
            claimCount: 10,
            previousReportJson: null,
            videoPlanText: prompterPlan);

        // 1. Prompt içine hem [YAZAR-TEZİ] katmanı hem de deterministik şüpheli '%37' uyarısı enjekte edilmiş olmalı
        capturedQcInput.Should().NotBeNull();
        capturedQcInput.Should().Contain("[YAZAR-TEZİ");
        capturedQcInput.Should().Contain("tefekkür boşluğu");
        capturedQcInput.Should().Contain("%37");

        // 2. LLM yanlışlıkla "desteklenmedi" dese bile VeriScore C# tez koruması yazarın felsefi tezini "desteklendi" ([YAZAR-TEZİ]) yapmalı,
        //    buna karşılık kaynakta geçmeyen uydurma %37 istatistiği "desteklenmedi" olarak yakalanmalı!
        using var reportDoc = JsonDocument.Parse(qcResult.DetayliRaporJson);
        var items = reportDoc.RootElement.EnumerateArray().ToList();

        var thesisItem = items.Single(x =>
            x.GetProperty("iddia").GetString()!.Contains("tefekkür boşluğu", StringComparison.OrdinalIgnoreCase));
        thesisItem.GetProperty("iddiaTuru").GetString().Should().Be("yazar_tezi_yorumu");
        thesisItem.GetProperty("durum").GetString().Should().Be("desteklendi");
        thesisItem.GetProperty("kanitKodu").GetString().Should().Be("[YAZAR-TEZİ]");

        var fabricatedStatItem = items.Single(x =>
            x.GetProperty("iddia").GetString()!.Contains("%37", StringComparison.OrdinalIgnoreCase));
        fabricatedStatItem.GetProperty("iddiaTuru").GetString().Should().Be("olgusal_kaynak_iddiasi");
        fabricatedStatItem.GetProperty("durum").GetString().Should().Be("desteklenmedi");

        qcResult.DesteklenenSayisi.Should().Be(2);
        qcResult.DesteklenmeyenSayisi.Should().Be(1);
    }

    [Fact]
    public async Task QualityCheckConsumer_AutoRefine_ShouldPreserveAuthorPhilosophicalThesis_AndCleanFabricated37PercentStatistic()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"veriscore-autorefine-{Guid.NewGuid()}")
            .Options;
        using var db = new AppDbContext(options);

        var egitimId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var chunk1Id = Guid.NewGuid();
        var chunk2Id = Guid.NewGuid();

        db.Egitimler.Add(new Egitim { Id = egitimId, Ad = "Felsefe ve Teknoloji" });
        db.ContentRequests.Add(new ContentRequest
        {
            Id = requestId,
            EgitimId = egitimId,
            Konu = AuthorThesisText,
            EkTonTalimati = "Pedagojik benzetmeler ve kavramsal sentez korunsun.",
            Durum = ContentRequestDurumu.QcYapiliyor,
            GeneratedContent = new GeneratedContent
            {
                Id = Guid.NewGuid(),
                ContentRequestId = requestId,
                ArastirmaOzeti =
                    "## 1. Biyolojik ve Kelami Zemin\n" +
                    "Sapolsky nöral geçmişin karar süreçlerini etkilediğini tartışır [K1]. Gazzâlî ise kesb ile irade mertebesini açıklar [K2].\n\n" +
                    "## 2. Arzu Mimarisi ve Tefekkür Boşluğu\n" +
                    "Modern algoritmik yönlendirme bireyin arzu mimarisini içeriden yeniden kurar; gerçek özgürlük dürtü ile eylem arasına bilinçli bir tefekkür boşluğu yerleştirebilmektir. " +
                    "Öte yandan küresel araştırmalarda gençlerin %37'si algoritmik akışta iradelerini tamamen yitirdiklerini beyan etmiştir.",
                VideoPlani =
                    "## Sahne 1: Tefekkür Boşluğu\n" +
                    "### 1. [Görsel & Edit Yönergesi]\n- Kamera: Orta plan\n" +
                    "### 2. [Prompter Metni (Aynen Okunacak)]\n" +
                    "> \"Algoritmalar arzu mimarimizi içeriden yeniden kurar; çözüm kendi tefekkür boşluğumuzu inşa etmektir. Araştırmalara göre kullanıcıların %37'si bu döngüyü fark edemiyor.\"",
                KullanilanKaynaklar = JsonSerializer.Serialize(new[] { chunk1Id, chunk2Id }),
                LlmModel = "argon"
            }
        });
        await db.SaveChangesAsync();

        var scBuilderMock = new Mock<ISourceContextBuilder>();
        scBuilderMock.Setup(b => b.BuildFromChunkIdsAsync(egitimId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SourceContextResult
            {
                ContextText = GroundedSourceContext,
                UsedChunkIds = new List<Guid> { chunk1Id, chunk2Id }
            });

        var capturedRefineInstructions = new List<string>();
        var synthMock = new Mock<ISynthesisProvider>();
        synthMock.SetupGet(s => s.ActiveModelName).Returns("argon");

        // İlk QC: Yazar tezi "yazar_tezi_yorumu" ("desteklendi"), uydurma %37 ise "olgusal_kaynak_iddiasi" ("desteklenmedi")
        synthMock.Setup(s => s.BatchQualityCheckAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                [
                  {
                    "iddia": "Sapolsky nöral geçmişin karar süreçlerini etkilediğini tartışır.",
                    "iddiaTuru": "olgusal_kaynak_iddiasi",
                    "durum": "desteklendi",
                    "kanitKodu": "[K1]",
                    "kaynakAlani": "ArastirmaOzeti",
                    "aciklama": "[K1] ile uyumlu."
                  },
                  {
                    "iddia": "Modern algoritmik yönlendirme bireyin arzu mimarisini içeriden yeniden kurar; gerçek özgürlük dürtü ile eylem arasına bilinçli bir tefekkür boşluğu yerleştirebilmektir.",
                    "iddiaTuru": "yazar_tezi_yorumu",
                    "durum": "desteklendi",
                    "kanitKodu": "[YAZAR-TEZİ]",
                    "kaynakAlani": "ArastirmaOzeti",
                    "aciklama": "Yazarın özgün felsefi/pedagojik tezi."
                  },
                  {
                    "iddia": "Gençlerin ve kullanıcıların %37'si algoritmik akışta iradelerini tamamen yitirdiklerini beyan etmiştir.",
                    "iddiaTuru": "olgusal_kaynak_iddiasi",
                    "durum": "desteklenmedi",
                    "kanitKodu": "-",
                    "kaynakAlani": "ArastirmaOzeti",
                    "aciklama": "Kaynaklarda %37 gibi bir istatistik bulunmamaktadır."
                  }
                ]
                """);

        // Auto-Refine: Talimatta SADECE %37 olgusal uydurmasının temizlenmesi istenmeli; yazar tezi korunmalı!
        synthMock.Setup(s => s.ReviseContentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, CancellationToken>((_, instruction, _, _, _, _) =>
            {
                capturedRefineInstructions.Add(instruction);
            })
            .ReturnsAsync((string original, string _, string contentType, string _, string _, CancellationToken _) =>
            {
                if (contentType.Contains("Plan", StringComparison.OrdinalIgnoreCase))
                {
                    return original.Replace(" Araştırmalara göre kullanıcıların %37'si bu döngüyü fark edemiyor.", "");
                }
                return original.Replace(" Öte yandan küresel araştırmalarda gençlerin %37'si algoritmik akışta iradelerini tamamen yitirdiklerini beyan etmiştir.", "");
            });

        // Re-QC: %37 temizlendikten ve yazar tezi korunduktan sonra %100 desteklendi
        synthMock.Setup(s => s.ReQualityCheckAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                [
                  {
                    "iddia": "Sapolsky nöral geçmişin karar süreçlerini etkilediğini tartışır.",
                    "iddiaTuru": "olgusal_kaynak_iddiasi",
                    "durum": "desteklendi",
                    "kanitKodu": "[K1]",
                    "kaynakAlani": "ArastirmaOzeti",
                    "aciklama": "[K1] ile doğrulandı."
                  },
                  {
                    "iddia": "Modern algoritmik yönlendirme bireyin arzu mimarisini içeriden yeniden kurar; gerçek özgürlük dürtü ile eylem arasına bilinçli bir tefekkür boşluğu yerleştirebilmektir.",
                    "iddiaTuru": "yazar_tezi_yorumu",
                    "durum": "desteklendi",
                    "kanitKodu": "[YAZAR-TEZİ]",
                    "kaynakAlani": "VideoPlani",
                    "aciklama": "[YAZAR-TEZİ] kapsamında eksiksiz korundu."
                  }
                ]
                """);

        var qcService = new QualityCheckService(
            synthMock.Object,
            NullLogger<QualityCheckService>.Instance,
            new DeterministicClaimScanner());

        var consumer = new QualityCheckConsumer(
            db,
            synthMock.Object,
            new Mock<ILogService>().Object,
            NullLogger<QualityCheckConsumer>.Instance,
            scBuilderMock.Object,
            qcService);

        var consumeCtx = new Mock<ConsumeContext<ContentGeneratedEvent>>();
        consumeCtx.Setup(c => c.Message).Returns(new ContentGeneratedEvent
        {
            ContentRequestId = requestId,
            EgitimId = egitimId,
            Konu = AuthorThesisText
        });
        consumeCtx.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        consumeCtx.Setup(c => c.Publish(It.IsAny<ContentProgressEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        consumeCtx.Setup(c => c.Publish(It.IsAny<ContentReadyEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await consumer.Consume(consumeCtx.Object);

        // 1. Auto-Refine hem Özet hem Video Planı için çağrılmış, silinecekler listesinde SADECE '%37' yer almış,
        //    yazarın 'tefekkür boşluğu / arzu mimarisi' tezi silinecekler listesine GİRMEMİŞ olmalı!
        capturedRefineInstructions.Should().HaveCount(2);
        foreach (var instr in capturedRefineInstructions)
        {
            instr.Should().Contain("[YAZAR-TEZİ]");
            instr.Should().Contain("%37");
            instr.Should().NotContain("tefekkür boşluğu yerleştirebilmektir");
        }

        // 2. Güncellenen nihai metinde yazarın özgün felsefi/pedagojik tezi KORUNMUŞ, uydurma %37 istatistiği TEMİZLENMİŞ olmalı!
        db.ChangeTracker.Clear();
        var updatedReq = await db.ContentRequests
            .Include(r => r.GeneratedContent)
            .Include(r => r.QcResult)
            .Include(r => r.Versions)
            .SingleAsync(r => r.Id == requestId);

        updatedReq.Durum.Should().Be(ContentRequestDurumu.Tamamlandi);
        updatedReq.GeneratedContent!.ArastirmaOzeti.Should().Contain("arzu mimarisini içeriden yeniden kurar");
        updatedReq.GeneratedContent.ArastirmaOzeti.Should().Contain("tefekkür boşluğu");
        updatedReq.GeneratedContent.ArastirmaOzeti.Should().NotContain("%37");

        updatedReq.GeneratedContent.VideoPlani.Should().Contain("tefekkür boşluğumuzu inşa etmektir");
        updatedReq.GeneratedContent.VideoPlani.Should().NotContain("%37");

        updatedReq.QcResult.Should().NotBeNull();
        updatedReq.QcResult!.DesteklenmeyenSayisi.Should().Be(0);
        updatedReq.QcResult.GuvenSkorYuzde.Should().Be(100m);
        updatedReq.Versions.Should().HaveCount(2, "V1 ilk üretim ve V2 Tez-Korumalı Auto-Refine kaydedilmeli");
    }
}
