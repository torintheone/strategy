using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Diplomacy.Systems;

/// <summary>Месяц дипломатии: память о событиях тает, отношения пересчитываются, договоры действуют. MODEL.md, «Дипломатия».</summary>
public sealed class DiplomacySystem : ISimSystem
{
    public string Name => "Дипломатия";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world) => DiplomacyModel.Update(world);
}
