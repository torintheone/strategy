using Politeconomy.Sim.Data;
using Politeconomy.Sim.Demography;
using Politeconomy.Sim.Economy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Save;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim;

/// <summary>Новая партия или загрузка: мир, экономика, население, классы, разогрев и все системы модели.</summary>
public static class GameSetup
{
    public sealed record Start(Simulation Simulation, WarmupReport? Warmup);

    public static Start NewGame(GameData data, ulong? seed = null)
    {
        var world = WorldFactory.Create(data, seed);
        DemographyModel.Setup(world);
        EconomyModel.Setup(world);
        WarmupReport? report = null;
        if (data.Scenario.WarmupMonths > 0)
        {
            // В разогреве живёт и население (рабочая сила растёт, как в данных), потом и оно возвращается к данным.
            report = EconomyModel.Warmup(world, data.Scenario.WarmupMonths, [.. EconomyModel.Systems(), .. DemographyModel.Systems()]);
            var economy = world.Economy;
            DemographyModel.Setup(world);
            world.Economy = economy;
        }
        Education.EducationModel.Setup(world);
        SocietyModel.Setup(world);
        GovernmentModel.Setup(world);
        Politics.PoliticsModel.Setup(world);
        LawModel.Setup(world);
        Health.HealthModel.Setup(world);
        Politics.InterestGroupsModel.Setup(world);
        Politics.ProtestModel.Setup(world);
        world.Budgets = new Budget.WorldBudgets();
        History.HistoryModel.Setup(world);
        return new Start(new Simulation(world, Systems()), report);
    }

    /// <summary>Все системы модели: законы, экономика, демография, классы, правительство, политика (порядок внутри месяца — по фазам).</summary>
    public static IReadOnlyList<ISimSystem> Systems() =>
        [.. LawModel.Systems(), .. EconomyModel.Systems(), new Budget.Systems.BudgetCycleSystem(), .. DemographyModel.Systems(),
            .. Education.EducationModel.Systems(), .. Health.HealthModel.Systems(), .. SocietyModel.Systems(), .. GovernmentModel.Systems(), .. Politics.PoliticsModel.Systems(),
            .. History.HistoryModel.Systems()];

    /// <summary>Подключить системы модели к миру из сохранения.</summary>
    public static Simulation Resume(World world)
    {
        world.Budgets ??= new Budget.WorldBudgets();
        return new(world, Systems());
    }

    public static Simulation Load(GameData data, string path) => Resume(SaveGame.Read(data, path));
}
