using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>
/// Мировой рынок: календарь модели, мировой цикл и цены сырья (нефть и газ, уголь и руды, продовольствие).
/// Цена сырья возвращается к норме и растёт в мировой подъём. MODEL.md, «Сырьё».
/// </summary>
public sealed class WorldMarketSystem : EconomySystem
{
    public override string Name => "Мировой рынок";
    public override SimPhase Phase => SimPhase.Economy;

    protected override void Run(World world, WorldEconomy economy, EconomyBalance balance)
    {
        if (!economy.Warmup)
            economy.Months++;

        var total = economy.Countries.Values.Sum(e => e.GdpUsd);
        double gap = 0, inflation = 0;
        foreach (var country in world.Countries)
        {
            var e = economy[country.Id];
            e.WorldShare = e.GdpUsd / total;
            gap += e.WorldShare * e.Gap;
            inflation += e.WorldShare * e.Inflation;
        }
        economy.WorldGap = gap;
        economy.WorldInflation = inflation;

        var b = balance.Commodities;
        economy.OilPrice = Step(economy.OilPrice, b.OilShock);
        economy.OresPrice = Step(economy.OresPrice, b.OresShock);
        economy.FoodPrice = Step(economy.FoodPrice, b.FoodShock);

        double Step(double price, double sigma)
        {
            var log = Math.Log(price);
            log += b.Reversion * (b.WorldDemand * gap - log) + Shock(world, economy, sigma);
            return Math.Exp(Math.Clamp(log, -2.5, 2.5));
        }
    }
}
