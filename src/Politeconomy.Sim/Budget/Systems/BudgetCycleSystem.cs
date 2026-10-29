using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Budget.Systems;

/// <summary>Бюджетный цикл страны игрока: проект, голосования, вступление в силу, поправки. MODEL.md, «Бюджетный цикл».</summary>
public sealed class BudgetCycleSystem : ISimSystem
{
    public string Name => "Бюджетный цикл";
    public SimPhase Phase => SimPhase.Budget;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world) => BudgetCycle.Step(world);
}
