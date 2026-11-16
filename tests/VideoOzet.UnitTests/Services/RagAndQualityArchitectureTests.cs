using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Pgvector;
using System.Net;
using System.Text;
using System.Text.Json;
using VideoOzet.Business.Infrastructure.AI;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.UnitTests.Services;

public class RagAndQualityArchitectureTests
{
    private static AppDbContext CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void ContextualTextChunker_ShouldSplitSentencesWithOverlap_AndTagTrailingReferencesAsKaynakca()
    {
        var chunker = new ContextualTextChunker();
        var bodySentences = string.Join(" ", Enumerable.Range(1, 70)
            .Select(i => $"Özgür irade tartışmasının {i}. argümanı nedensellik zinciriyle açıklanır."));

        var fullText =
            "## Giriş ve Kavramsal Çerçeve\n" +
            bodySentences + "\n\n" +
            bodySentences + "\n\n" +
            "KAYNAKÇA\n" +
            "Akbulut, B. (2023). Yapay Zeka ve Ceza Hukuku. Seçkin Yayınevi.\n" +
            "Sapolsky, R. (2023). Determined: A Science of Life without Free Will. Penguin Press.";

        var opts = new ContextualChunkOptions(
            KaynakBaslik: "Ozgur_Irade_Makale.pdf",
            KaynakTuru: "Makale/PDF",
            Dil: "tr",
            KullanimTuru: KaynakKullanimTuru.DogrudanReferans,
            TargetWords: 120,
            OverlapWords: 20);

        var chunks = chunker.ChunkDocument(fullText, opts);

        chunks.Should().HaveCountGreaterThan(2);
        chunks.Where(c => c.ChunkTipi == ChunkTipi.HamMetin).Should().NotBeEmpty();
        chunks.Where(c => c.ChunkTipi == ChunkTipi.Kaynakca).Should().NotBeEmpty();
        chunks.All(c => c.ContextualizedText.Contains("[Kaynak: Ozgur_Irade_Makale.pdf", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        chunks.All(c => c.ContextualizedText.Contains("Dil: TR")).Should().BeTrue();
    }

    [Fact]
    public void ContextualTextChunker_ShouldPreserveWhisperTimestamps_AndProportionalVideoFallback()
    {
        var chunker = new ContextualTextChunker();
        var segmentsJson = JsonSerializer.Serialize(new[]
        {
            new { start = 0.0, end = 32.5, text = "Birinci konuşma bölümü biyolojik belirlenimciliği ele alır." },
            new { start = 33.0, end = 68.4, text = "İkinci konuşma bölümü dopamin ve karar alma ilişkisini inceler." }
        });

        var opts = new ContextualChunkOptions(
            KaynakBaslik: "Sapolsky_Lecture.mp4",
            KaynakTuru: "Video Transkripti",
            Dil: "en",
            KullanimTuru: KaynakKullanimTuru.SadeceArgumanVeMantik,
            TargetWords: 250,
            OverlapWords: 30,
            VideoDurationSeconds: 120);

        var whisperChunks = chunker.ChunkTranscript("fallback", segmentsJson, opts);
        whisperChunks.Should().HaveCount(1);
        whisperChunks[0].ChunkTipi.Should().Be(ChunkTipi.Transkript);
        whisperChunks[0].StartTimeMs.Should().Be(0);
        whisperChunks[0].EndTimeMs.Should().Be(68400);

        var longTranscript = string.Join(" ", Enumerable.Range(1, 90)
            .Select(i => $"Nörobiyolojik süreçler {i}. gözlemde seçimleri etkiler."));
        var fallbackChunks = chunker.ChunkTranscript(longTranscript, null, opts with { TargetWords = 80, OverlapWords = 15 });

        fallbackChunks.Should().HaveCountGreaterThan(1);
        fallbackChunks.All(c => c.EndTimeMs > c.StartTimeMs).Should().BeTrue();
        fallbackChunks[^1].EndTimeMs.Should().BeLessThanOrEqualTo(120000);
    }

    [Fact]
    public async Task OllamaEmbeddingProvider_ShouldThrowInNormalMode_AndFallbackOnlyInStandaloneMode()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var httpClient = new HttpClient(handlerMock.Object);
        var normalConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OLLAMA_URL"] = "http://localhost:11435",
                ["StandaloneMode"] = "false"
            })
            .Build();

        var normalProvider = new OllamaEmbeddingProvider(normalConfig, NullLogger<OllamaEmbeddingProvider>.Instance, httpClient);
        Func<Task> act = () => normalProvider.GenerateEmbeddingAsync("test metni");
        await act.Should().ThrowAsync<InvalidOperationException>();

        var standaloneConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OLLAMA_URL"] = "http://localhost:11435",
                ["StandaloneMode"] = "true"
            })
            .Build();

        var standaloneProvider = new OllamaEmbeddingProvider(standaloneConfig, NullLogger<OllamaEmbeddingProvider>.Instance, new HttpClient(handlerMock.Object));
        var fallbackVector = await standaloneProvider.GenerateEmbeddingAsync("test metni");
        fallbackVector.Should().HaveCount(1024);
    }

    [Fact]
    public async Task QueryDecomposer_ShouldSplitOutline_AndExpandCrossLingualTerms()
    {
        var decomposer = new QueryDecomposer();
        var outline = """
            1. Felsefi ve İslami Perspektif: Gazzâlî'de kesb ve cüzî irade
            2. Nörobiyoloji: Sapolsky ve dopamin etkileri
            3. Dijital Dikkat Ekonomisi: Öneri algoritmaları
            """;

        var decomposed = await decomposer.DecomposeAsync(outline, new[] { "tr", "en" });

        decomposed.Should().HaveCount(3);
        decomposed[0].ExpandedKeywordsText.Should().Contain("ghazali");
        decomposed[1].ExpandedKeywordsText.Should().Contain("dopamine");
        decomposed[2].ExpandedKeywordsText.Should().Contain("recommender system");
    }

    [Fact]
    public async Task SourceContextBuilder_ShouldReturnExactChunkIds_AndEnforcePerSourceQuota()
    {
        var egitimId = Guid.NewGuid();
        using var db = CreateInMemoryDb($"rag-builder-{Guid.NewGuid()}");

        var dummyVec = new Vector(Enumerable.Repeat(0.01f, 1024).ToArray());
        var targetChunkIds = new List<Guid>();

        for (int s = 0; s < 10; s++)
        {
            var docId = Guid.NewGuid();
            bool isGazzali = s % 2 == 0;
            var title = isGazzali ? $"Kaynak{s}_Gazzali.pdf" : $"Kaynak{s}_Sapolsky.pdf";
            db.Dokumanlar.Add(new Dokuman
            {
                Id = docId,
                EgitimId = egitimId,
                DosyaAdi = title,
                IslemDurumu = VideoIslemDurumu.Tamamlandi,
                KullanimTuru = isGazzali ? KaynakKullanimTuru.DogrudanReferans : KaynakKullanimTuru.SadeceArgumanVeMantik,
                DokumanMetin = new DokumanMetin
                {
                    Id = Guid.NewGuid(),
                    DokumanId = docId,
                    HamMetin = new string(isGazzali ? 'g' : 's', 10000),
                    OrijinalDil = isGazzali ? "tr" : "en"
                }
            });

            for (int i = 0; i < 5; i++)
            {
                var c = new VideoChunkDocument
                {
                    Id = Guid.NewGuid(),
                    EgitimId = egitimId,
                    DokumanId = docId,
                    ChunkIndex = i,
                    ChunkTipi = ChunkTipi.HamMetin,
                    KaynakBaslik = title,
                    BolumBasligi = $"Bölüm {i}",
                    KaynakDil = isGazzali ? "tr" : "en",
                    Text = isGazzali
                        ? $"Gazzali irade ve nedensellik açıklaması kaynak {s} parça {i}. " + new string('a', 600)
                        : $"Sapolsky neurobiology dopamine hormones choice source {s} part {i}. " + new string('b', 600),
                    Embedding = dummyVec
                };
                db.VideoChunkDocuments.Add(c);
                if (s == 0 && i < 2) targetChunkIds.Add(c.Id);
            }
        }

        await db.SaveChangesAsync();

        var embedMock = new Mock<IEmbeddingProvider>();
        embedMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
            .ReturnsAsync((List<string> texts, string _) => texts.Select(_ => Enumerable.Repeat(0.01f, 1024).ToArray()).ToList());

        var builder = new SourceContextBuilder(db, embedMock.Object, NullLogger<SourceContextBuilder>.Instance, new QueryDecomposer());

        var exactResult = await builder.BuildFromChunkIdsAsync(egitimId, targetChunkIds);
        exactResult.UsedChunkIds.Should().BeEquivalentTo(targetChunkIds);
        exactResult.Evidences.Should().HaveCount(2);

        var hybridResult = await builder.BuildContextAsync(egitimId, new[] { "1. Gazzali irade\n2. Sapolsky dopamine" }, maxChars: 18000);
        hybridResult.Evidences.Should().NotBeEmpty();
        var perSourceMax = hybridResult.Evidences.GroupBy(e => e.DokumanId ?? e.VideoId).Max(g => g.Count());
        perSourceMax.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task DeterministicClaimScanner_AndQualityCheckService_ShouldCatchFabricatedStats_AndAvoidFake100()
    {
        var scanner = new DeterministicClaimScanner();
        var sourceContext = "=== [K1] ===\nAkbulut (2023) çalışmasında yapay zekanın kusur oranı %45 olarak tartışılır.";
        var generated = "Araştırmada kusur oranı %45 olarak tartışılır. Facebook kullanıcılarının %70'i önerilerle vakit geçirir. TikTok kullanıcılarının %80'i algoritmalarla yönlendirilir.";

        var anomalies = scanner.FindUngroundedNumbersAndCitations(generated, sourceContext);
        anomalies.Should().HaveCount(2);
        anomalies.Select(a => a.MatchedValue).Should().Contain(new[] { "%70", "%80" });
        anomalies.Select(a => a.MatchedValue).Should().NotContain("%45");

        var synthMock = new Mock<ISynthesisProvider>();
        synthMock.Setup(s => s.BatchQualityCheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "iddialar": [
                    { "iddia": "Kusur oranı %45 olarak tartışılır.", "durum": "desteklendi", "kanitKodu": "K1", "kaynakAlani": "Araştırma Özeti", "aciklama": "[K1] destekler." }
                  ]
                }
                """);

        var qcService = new QualityCheckService(synthMock.Object, NullLogger<QualityCheckService>.Instance, scanner);

        // 1. Boş kaynak -> asla sahte %100 vermemeli
        var emptyQc = await qcService.EvaluateAsync("Herhangi bir iddia", "");
        emptyQc.GuvenSkorYuzde.Should().Be(0m);
        emptyQc.DesteklenenSayisi.Should().Be(0);
        emptyQc.BelirsizSayisi.Should().Be(1);

        // 2. Uydurma %70/%80 istatistikleri deterministik koruma ile desteklenmedi olarak işaretlenmeli
        var guardedQc = await qcService.EvaluateAsync(generated, sourceContext);
        guardedQc.DesteklenmeyenSayisi.Should().Be(2);
        guardedQc.DesteklenenSayisi.Should().Be(1);
        guardedQc.GuvenSkorYuzde.Should().BeLessThan(100m);

        // 3. Groq kompaktlama K1..K10 bloklarının hiçbirini tamamen düşürmemeli
        var blocks = string.Join("\n\n", Enumerable.Range(1, 10)
            .Select(i => $"=== [K{i}] | Kaynak: Doc{i}.pdf ===\n" + new string((char)('a' + i - 1), 900)));
        var prompt = $"GÖREV: Analiz yap.\n\n{blocks}\n\nÇOK KRİTİK KURALLAR: [K#] kodlarıyla yanıtla.";

        var compacted = ClaudeSynthesisProvider.CompactPromptForGroq(prompt, 4500);
        for (int i = 1; i <= 10; i++)
        {
            compacted.Should().Contain($"[K{i}]");
        }
        compacted.Should().Contain("ÇOK KRİTİK KURALLAR");
    }
}
