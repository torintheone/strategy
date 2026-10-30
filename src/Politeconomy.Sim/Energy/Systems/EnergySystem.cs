using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Energy.Systems;

/// <summary>Месяц энергетики: спрос, стройка и списание станций, выработка, нехватка, цена. MODEL.md, «Энергетика».</summary>
public sealed class EnergySystem : ISimSystem
{
    public string Name => "Энергетика";
    public SimPhase Phase => SimPhase.Economy;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var energy = world.Energy ?? throw new InvalidOperationException("Энергетика не создана: вызовите EnergyModel.Setup");
        foreach (var country in world.Countries)
            EnergyModel.Update(world, country, energy[country.Id]);
        var demand = energy.Countries.Values.Sum(c => c.Demand);
        energy.CostRatio = demand > 0 ? energy.Countries.Values.Sum(c => c.Demand * c.CostRatio) / demand : 1;
    }
}
