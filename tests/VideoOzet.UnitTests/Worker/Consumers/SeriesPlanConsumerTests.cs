using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Consumers;
using Xunit;

namespace VideoOzet.UnitTests.Worker.Consumers;

/// <summary>
/// Regresyon testleri: Seri planı bölümleri kaydedilirken alınan
/// "expected to affect 1 row(s), but actually affected 0 row(s)" (DbUpdateConcurrencyException) hatası.
/// Kök neden: istemci tarafında üretilen Guid anahtarların EF tarafından store-generated sanılması.
/// </summary>
public class SeriesPlanConsumerTests
{
    private const string PlanJson = @"{
        ""VideoSayisi"": ""3"",
        ""VarsayilanVideoSuresiDk"": 12.0,
        ""OneridenFarkli"": false,
        ""SeriHaritasi"": [
            { ""BolumNo"": 1, ""CalismaBasligi"": ""Giriş"", ""HedefSureDk"": 10, ""AnaFikir"": ""A"", ""Konular"": [""K1""] },
            { ""BolumNo"": 1, ""CalismaBasligi"": ""Önermeler"", ""HedefSureDk"": ""15"", ""AnaFikir"": ""B"", ""Konular"": [""K2""] },
            { ""CalismaBasligi"": ""Kıyas"", ""AnaFikir"": ""C"", ""Konular"": [""K3""] }
        ],
        ""DisaridaBirakilanlar"": []
    }";

    private static (AppDbContext db, Guid requestId, Guid egitimId) CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);

        var egitimId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        db.Egitimler.Add(new Egitim { Id = egitimId, Ad = "Mantık" });
        db.ContentRequests.Add(new ContentRequest { Id = requestId, EgitimId = egitimId, Konu = "Klasik Mantık" });
        db.KonuAnalizleri.Add(new KonuAnalizi
        {
            ContentRequestId = requestId,
            AnaFikir = "Mantığa giriş",
            BeklenenKonularJson = "[]",
            KonuHaritasiJson = "[]",
            KaynaktaOlmayanlarJson = "[]",
            OnerilenVideoSayisi = 3,
            OneriSuresiDk = 10,
            Durum = AnalizDurumu.Tamamlandi
        });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return (db, requestId, egitimId);
    }

    private static (SeriesPlanConsumer consumer, Mock<ConsumeContext<SeriesPlanRequestedEvent>> ctx, List<object> published)
        CreateConsumer(AppDbContext db, Guid requestId, Guid egitimId)
    {
        var synthesis = new Mock<ISynthesisProvider>();
        synthesis.Setup(s => s.GenerateSeriesPlanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlanJson);

        var mapper = new Mock<ISourceTopicMapper>();
        mapper.Setup(m => m.BuildTopicDigestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[VİDEO: Giriş]\n- Mantık nedir: ... (~5 dk)");

        var consumer = new SeriesPlanConsumer(db, synthesis.Object, mapper.Object,
            new Mock<ILogService>().Object, new Mock<ILogger<SeriesPlanConsumer>>().Object);

        var published = new List<object>();
        var ctx = new Mock<ConsumeContext<SeriesPlanRequestedEvent>>();
        ctx.Setup(c => c.Message).Returns(new SeriesPlanRequestedEvent { ContentRequestId = requestId, EgitimId = egitimId });
        ctx.Setup(c => c.Publish(It.IsAny<PipelineProgressEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineProgressEvent, CancellationToken>((e, _) => published.Add(e)).Returns(Task.CompletedTask);
        ctx.Setup(c => c.Publish(It.IsAny<SeriesPlanGeneratedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SeriesPlanGeneratedEvent, CancellationToken>((e, _) => published.Add(e)).Returns(Task.CompletedTask);
        ctx.Setup(c => c.Publish(It.IsAny<ContentErrorEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ContentErrorEvent, CancellationToken>((e, _) => published.Add(e)).Returns(Task.CompletedTask);

        return (consumer, ctx, published);
    }

    [Fact]
    public async Task Consume_NewPlan_ShouldPersistAllEpisodes_WithSequentialNumbers()
    {
        var (db, requestId, egitimId) = CreateDb();
        var (consumer, ctx, published) = CreateConsumer(db, requestId, egitimId);

        await consumer.Consume(ctx.Object);

        var err = published.OfType<ContentErrorEvent>().FirstOrDefault();
        err.Should().BeNull(err?.HataMesaji);

        db.ChangeTracker.Clear();
        var plan = await db.SeriPlanlari.Include(p => p.SeriBolumler).SingleAsync(p => p.ContentRequestId == requestId);
        plan.Durum.Should().Be(SeriPlanDurumu.OnayBekliyor);
        plan.VideoSayisi.Should().Be(3);
        plan.VarsayilanVideoSuresiDk.Should().Be(12);
        plan.SeriBolumler.Select(b => b.BolumNo).OrderBy(n => n).Should().Equal(1, 2, 3);
        plan.SeriBolumler.Single(b => b.BolumNo == 2).HedefSureDk.Should().Be(15);
        plan.SeriBolumler.Single(b => b.BolumNo == 3).HedefSureDk.Should().Be(12);

        published.OfType<SeriesPlanGeneratedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Consume_ExistingUnapprovedPlan_ShouldReplaceEpisodes()
    {
        var (db, requestId, egitimId) = CreateDb();

        // Önceki başarısız denemeden kalmış, onaylanmamış plan + eski bölüm
        var oldPlan = new SeriPlani { ContentRequestId = requestId, PlanNo = 1, Durum = SeriPlanDurumu.Hata };
        db.SeriPlanlari.Add(oldPlan);
        db.SeriBolumler.Add(new SeriBolum { SeriPlaniId = oldPlan.Id, BolumNo = 1, CalismaBasligi = "Eski" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (consumer, ctx, published) = CreateConsumer(db, requestId, egitimId);
        await consumer.Consume(ctx.Object);

        var err = published.OfType<ContentErrorEvent>().FirstOrDefault();
        err.Should().BeNull(err?.HataMesaji);

        db.ChangeTracker.Clear();
        var plans = await db.SeriPlanlari.Include(p => p.SeriBolumler).Where(p => p.ContentRequestId == requestId).ToListAsync();
        plans.Should().ContainSingle();
        plans[0].Id.Should().Be(oldPlan.Id);
        plans[0].Durum.Should().Be(SeriPlanDurumu.OnayBekliyor);
        plans[0].SeriBolumler.Should().HaveCount(3);
        plans[0].SeriBolumler.Should().NotContain(b => b.CalismaBasligi == "Eski");
    }

    [Fact]
    public async Task Model_ChildAddedViaNavigation_ShouldBeInserted_NotUpdated()
    {
        // Eski SeriesPlanConsumer deseni: kaydedilmiş (tracked) plana navigation üzerinden bölüm eklemek.
        var (db, requestId, _) = CreateDb();
        var plan = new SeriPlani { ContentRequestId = requestId, PlanNo = 1, Durum = SeriPlanDurumu.Olusturuluyor };
        db.SeriPlanlari.Add(plan);
        await db.SaveChangesAsync();

        var bolum = new SeriBolum { BolumNo = 1, CalismaBasligi = "Navigation ile eklendi" };
        plan.SeriBolumler.Add(bolum);
        db.ChangeTracker.DetectChanges();

        db.Entry(bolum).State.Should().Be(EntityState.Added);
        var save = async () => await db.SaveChangesAsync();
        await save.Should().NotThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public void Model_GuidPrimaryKeys_ShouldBeClientGenerated()
    {
        var (db, _, _) = CreateDb();

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var pk = entityType.FindPrimaryKey();
            if (pk == null || pk.Properties.Count != 1 || pk.Properties[0].ClrType != typeof(Guid)) continue;

            pk.Properties[0].ValueGenerated.Should().Be(
                Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never,
                $"{entityType.ClrType.Name}.Id istemci tarafında Guid.NewGuid() ile üretiliyor");
        }
    }
}
