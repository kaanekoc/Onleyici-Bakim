using Microsoft.EntityFrameworkCore;
using OnleyiciBakim.Data.BakimYonetimi;
using OnleyiciBakim.Data.BakimYonetimi.Entities;
using OnleyiciBakim.Options;
using OnleyiciBakim.Services.Features;
using Xunit;

namespace OnleyiciBakim.Tests;

public sealed class BakimYonetimiMachineFeatureServiceTests
{
    [Fact]
    public async Task HistoricalSnapshot_UsesLatestObservationInsteadOfWallClock()
    {
        await using var db = new BakimYonetimiDbContext(
            new DbContextOptionsBuilder<BakimYonetimiDbContext>()
                .UseInMemoryDatabase($"historical-features-{Guid.NewGuid():N}").Options);
        db.Makineler.Add(new Makineler
        {
            MakineId = "M-1", MakineKodu = "M-1", MakineAdi = "Makine",
            FirmaId = "F-1", SubeId = "S-1", DepartmanId = "D-1", HatId = "H-1",
            Model = "model1", KritikSeviye = "Yüksek", KurulumYili = 2010,
            SonBakimTarihi = new DateTime(2016, 1, 20), Aktif = true
        });
        db.BakimKayitlari.Add(new BakimKayitlari
        {
            BakimId = "B-1", MakineId = "M-1", BakimTarihi = new DateTime(2016, 1, 20),
            SonrakiBakimTarihi = new DateTime(2016, 2, 20)
        });
        db.ArizaKartlari.Add(new ArizaKartlari
        {
            ArizaId = "A-1", MakineId = "M-1", ArizaTarihi = new DateTime(2016, 1, 25),
            ArizaTuru = "Rulman", DurusSuresiDk = 120
        });
        db.TelemetriGunluk.Add(new TelemetriGunluk
        {
            Id = 1, MakineId = "M-1", Tarih = new DateTime(2016, 1, 31),
            VoltOrt = 170, DevirOrt = 450, BasincOrt = 100, TitresimOrt = 40,
            AnomaliPuani = 7, UretimYogunluguYuzde = 75
        });
        await db.SaveChangesAsync();

        var service = new BakimYonetimiMachineFeatureService(
            db, Microsoft.Extensions.Options.Options.Create(new AlgorithmicRiskOptions()));
        var result = await service.BuildFeaturesAsync("M-1", new DateTime(2026, 8, 6));

        Assert.Equal(11, result.SonBakimdanGecenGun);
        Assert.Equal(1, result.ArizaSayisi30G);
        Assert.Equal(7, result.AnomaliPuani7G);
        Assert.False(result.IsMandatoryMaintenanceOverdue);
    }
}
