using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim.Tests;

public class RegionTests
{
    [Fact]
    public void RegionsStartUnderTheirOwners()
    {
        var world = WorldFactory.Create(TestData.Game);
        Assert.Equal(TestData.Game.Regions.Regions.Count, world.Regions.Count);
        Assert.All(world.Regions, r => Assert.Equal(r.Owner, r.Controller));
        Assert.Empty(WorldSnapshot.Capture(world, 1, true, 0).Occupied);
    }

    [Fact]
    public void RegionPopulationsAddUpToTheCountry()
    {
        var data = TestData.Game;
        var sums = data.Regions.Regions.GroupBy(r => r.Country).ToDictionary(g => g.Key, g => g.Sum(r => r.Population));
        foreach (var c in data.Countries.Countries)
        {
            var population = c.Stats["population"].Value;
            // Сумма может быть больше на население зависимых территорий (Гренландия у Дании).
            Assert.InRange(sums[c.Id] / population, 0.99, 1.3);
        }
    }

    [Fact]
    public void ControlCanBeTransferredAndReturned()
    {
        var world = WorldFactory.Create(TestData.Game);
        var crimea = world.Regions.Single(r => r.Owner == "UKR" && r.Name.Contains("Крым"));

        world.TransferControl(crimea.Id, "RUS");
        Assert.True(crimea.IsOccupied);
        Assert.Contains(crimea, world.RegionsControlledBy("RUS"));
        Assert.Equal("RUS", WorldSnapshot.Capture(world, 1, true, 0).Occupied[crimea.Id]);

        world.TransferControl(crimea.Id, "UKR");
        Assert.False(crimea.IsOccupied);
        Assert.Throws<KeyNotFoundException>(() => world.TransferControl(crimea.Id, "XXX"));
    }

    [Fact]
    public void OccupationSurvivesSaveAndLoad()
    {
        var world = WorldFactory.Create(TestData.Game);
        var region = world.Regions.First(r => r.Owner == "POL");
        world.TransferControl(region.Id, "BLR");

        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = SaveGame.Read(TestData.Game, stream);

        Assert.Equal("BLR", loaded.RegionById(region.Id)!.Controller);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
    }
}
