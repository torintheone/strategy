using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>
/// Рынок труда (до этапа 2 — одним агрегатом): безработица тянется к естественной, в спад растёт
/// по закону Оукена; зарплаты растут с ожидаемой инфляцией и производительностью. MODEL.md, «Рынок труда».
/// </summary>
public sealed class LaborMarketSystem : EconomySystem
{
    public override string Name => "Рынок труда";
    public override SimPhase Phase => SimPhase.LaborMarket;

    protected override void Run(World world, WorldEconomy economy, EconomyBalance balance)
    {
        var b = balance.Labor;
        foreach (var country in world.Countries)
        {
            var e = economy[country.Id];
            var target = e.NaturalUnemployment - b.Okun * e.Gap;
            e.Unemployment = Math.Clamp(e.Unemployment + b.Adjustment * (target - e.Unemployment), 0.005, 0.85);
            var wageGrowth = e.Expectations + e.PotentialGrowth - e.PopulationGrowth0 - b.WageSlack * (e.Unemployment - e.NaturalUnemployment);
            e.WageIndex *= Math.Exp(wageGrowth / 12);
        }
    }
}
