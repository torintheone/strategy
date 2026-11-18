using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>Общая часть систем экономики: доступ к состоянию и коэффициентам. Все системы — помесячные.</summary>
public abstract class EconomySystem : ISimSystem
{
    public abstract string Name { get; }
    public abstract SimPhase Phase { get; }
    public virtual SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var economy = world.Economy ?? throw new InvalidOperationException("Экономика не создана: вызовите EconomyModel.Setup");
        Run(world, economy, world.Data.Economy);
    }

    protected abstract void Run(World world, WorldEconomy economy, EconomyBalance balance);

    /// <summary>Случайный толчок: во время разогрева его нет, чтобы модель успокоилась в своём равновесии.</summary>
    protected static double Shock(World world, WorldEconomy economy, double sigma) =>
        economy.Warmup || sigma <= 0 ? 0 : sigma * world.Random.NextGaussian();

    /// <summary>Экономика-ориентир для курсов и мировых ставок — эмитент доллара.</summary>
    public static CountryEconomy Reference(WorldEconomy economy) => economy[ReferenceId(economy)];

    public static string ReferenceId(WorldEconomy economy) =>
        economy.Currencies.TryGetValue("USD", out var usd) ? usd.Home : economy.Countries.MaxBy(kv => kv.Value.GdpUsd).Key;
}
