using Politeconomy.Sim.Data;
using Politeconomy.Sim.Economy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim;

/// <summary>Новая партия или загрузка: мир, экономика, разогрев и все системы модели.</summary>
public static class GameSetup
{
    public sealed record Start(Simulation Simulation, WarmupReport? Warmup);

    public static Start NewGame(GameData data, ulong? seed = null)
    {
        var world = WorldFactory.Create(data, seed);
        EconomyModel.Setup(world);
        var report = data.Scenario.WarmupMonths > 0 ? EconomyModel.Warmup(world, data.Scenario.WarmupMonths) : null;
        return new Start(new Simulation(world, EconomyModel.Systems()), report);
    }

    /// <summary>Подключить системы модели к миру из сохранения.</summary>
    public static Simulation Resume(World world) => new(world, EconomyModel.Systems());

    public static Simulation Load(GameData data, string path) => Resume(SaveGame.Read(data, path));
}
