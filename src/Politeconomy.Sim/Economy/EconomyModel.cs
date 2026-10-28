using Politeconomy.Sim.Economy.Systems;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy;

/// <summary>Сборка модели экономики: системы в порядке MODEL.md, калибровка и разогрев.</summary>
public static class EconomyModel
{
    /// <summary>Системы экономики в порядке выполнения внутри месяца.</summary>
    public static IReadOnlyList<ISimSystem> Systems() =>
    [
        new WorldMarketSystem(),
        new ProductionSystem(),
        new DemandSystem(),
        new LaborMarketSystem(),
        new PriceSystem(),
        new MonetarySystem(),
        new ExchangeRateSystem(),
        new BudgetSystem(),
        new RatingSystem(),
        new FiscalPlanSystem(),
        new EconomyStatsSystem(),
    ];

    /// <summary>Создать экономику мира по данным и вывести стартовые показатели.</summary>
    public static void Setup(World world) => Start(world, EconomyCalibration.Calibrate(world));

    /// <summary>Поставить экономику в мир: вывести показатели месяца старта и записать его в историю.</summary>
    static void Start(World world, WorldEconomy economy)
    {
        world.Economy = economy;
        EconomyStatsSystem.Publish(world);
        foreach (var e in economy.Countries.Values)
            e.PushHistory();
        economy.PushHistory();
    }

    /// <summary>
    /// Разогрев: модель живёт несколько месяцев без случайных толчков и с замершим календарём,
    /// чтобы скрытые величины (ожидания, ставки, цикл) пришли в согласие друг с другом.
    /// Затем уровни (ВВП, цены, курсы, долг, население) и наблюдаемые показатели возвращаются к данным,
    /// а скрытые величины остаются разогретыми. Отчёт показывает, куда модель успела уйти от данных.
    /// </summary>
    public static WarmupReport Warmup(World world, int months)
    {
        if (world.Economy is null)
            Setup(world);
        var warmed = world.Economy!;
        warmed.Warmup = true;
        var monthly = Systems().Where(s => s.Frequency == SimFrequency.Monthly).ToList();
        for (var m = 0; m < months; m++)
            foreach (var system in monthly)
                system.Run(world);
        var report = WarmupReport.Measure(world, months);

        var fresh = EconomyCalibration.Calibrate(world);
        foreach (var (id, e) in fresh.Countries)
        {
            var w = warmed.Countries[id];
            e.Expectations = w.Expectations;
            e.GapCore = w.GapCore;
            e.Gap = w.GapCore;
            e.PrimaryDeficitSmooth = w.PrimaryDeficitSmooth;
        }
        foreach (var (id, cur) in fresh.Currencies)
        {
            var w = warmed.Currencies[id];
            cur.PolicyRate = w.PolicyRate;
            cur.RealRateDifference = w.RealRateDifference;
            foreach (var member in cur.Members)
                fresh[member].PolicyRate = cur.PolicyRate;
        }
        foreach (var country in world.Countries)
            RatingSystem.Assess(world.Data.Economy, fresh, fresh[country.Id]);
        Start(world, fresh);
        return report;
    }
}
