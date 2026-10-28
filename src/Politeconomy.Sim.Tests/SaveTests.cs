using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim.Tests;

public class SaveTests
{
    [Fact]
    public void SaveThenLoadGivesSameStateAndSameFuture()
    {
        var world = WorldFactory.Create(TestData.Game, 7);
        world.PlayerCountry = "BRA";
        new Simulation(world, [new RandomWalkSystem()]).StepDays(100);

        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = SaveGame.Read(TestData.Game, stream);

        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
        Assert.Equal("BRA", loaded.PlayerCountry);

        // Генератор случайных чисел тоже восстановлен: дальше обе партии идут одинаково.
        new Simulation(world, [new RandomWalkSystem()]).StepDays(50);
        new Simulation(loaded, [new RandomWalkSystem()]).StepDays(50);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
    }

    [Fact]
    public void SaveToFileIsCompressed()
    {
        var world = WorldFactory.Create(TestData.Game);
        var path = Path.Combine(Path.GetTempPath(), $"politeconomy-{Guid.NewGuid():N}.save");
        try
        {
            SaveGame.Write(world, path);
            Assert.True(new FileInfo(path).Length < 200_000);
            Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(SaveGame.Read(TestData.Game, path)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
