using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using Xunit;

namespace VideoOzet.UnitTests.Data;

public class SeriesPlanningDataTests
{
    private DbContextOptions<AppDbContext> CreateInMemoryDbOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task Can_Insert_And_Retrieve_Series_Planning_Entities()
    {
        // Arrange
        var options = CreateInMemoryDbOptions();
        
        using var context = new AppDbContext(options);
        
        var egitim = new Egitim { Ad = "Test" };
        var contentRequest = new ContentRequest 
        { 
            Egitim = egitim,
            Konu = "Test Konu",
            Mod = IcerikModu.Planli 
        };
        
        var konuAnalizi = new KonuAnalizi
        {
            ContentRequest = contentRequest,
            AnaFikir = "Test Ana Fikir",
            OnerilenVideoSayisi = 3,
            Durum = AnalizDurumu.Tamamlandi
        };
        
        var seriPlani = new SeriPlani
        {
            ContentRequest = contentRequest,
            PlanNo = 1,
            VideoSayisi = 3,
            Durum = SeriPlanDurumu.Taslak
        };
        
        var bolum1 = new SeriBolum
        {
            SeriPlani = seriPlani,
            BolumNo = 1,
            CalismaBasligi = "Bölüm 1"
        };
        
        var revizyon1 = new BolumRevizyonu
        {
            SeriBolum = bolum1,
            RevizyonNo = 1,
            Tip = RevizyonTipi.IlkUretim,
            ArastirmaOzeti = "Araştırma 1"
        };
        
        // Act
        context.Egitimler.Add(egitim);
        context.ContentRequests.Add(contentRequest);
        context.KonuAnalizleri.Add(konuAnalizi);
        context.SeriPlanlari.Add(seriPlani);
        context.SeriBolumler.Add(bolum1);
        context.BolumRevizyonlari.Add(revizyon1);
        
        await context.SaveChangesAsync();

        // Assert
        using var readContext = new AppDbContext(options);
        var dbRequest = await readContext.ContentRequests
            .Include(x => x.KonuAnalizi)
            .Include(x => x.SeriPlanlari)
                .ThenInclude(x => x.SeriBolumler)
                    .ThenInclude(x => x.Revizyonlar)
            .FirstOrDefaultAsync(x => x.Id == contentRequest.Id);
            
        dbRequest.Should().NotBeNull();
        dbRequest!.Mod.Should().Be(IcerikModu.Planli);
        
        dbRequest.KonuAnalizi.Should().NotBeNull();
        dbRequest.KonuAnalizi!.AnaFikir.Should().Be("Test Ana Fikir");
        
        dbRequest.SeriPlanlari.Should().HaveCount(1);
        var savedPlan = dbRequest.SeriPlanlari.First();
        savedPlan.VideoSayisi.Should().Be(3);
        
        savedPlan.SeriBolumler.Should().HaveCount(1);
        var savedBolum = savedPlan.SeriBolumler.First();
        savedBolum.CalismaBasligi.Should().Be("Bölüm 1");
        
        savedBolum.Revizyonlar.Should().HaveCount(1);
        var savedRev = savedBolum.Revizyonlar.First();
        savedRev.ArastirmaOzeti.Should().Be("Araştırma 1");
    }
}
