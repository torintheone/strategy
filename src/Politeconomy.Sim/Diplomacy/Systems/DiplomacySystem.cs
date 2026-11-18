using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Diplomacy.Systems;

/// <summary>
/// Дипломатия. 1-го числа память о событиях тает, отношения пересчитываются, договоры действуют; в остальные дни —
/// только если где-то сменилась власть. MODEL.md, «Дипломатия».
/// </summary>
public sealed class DiplomacySystem : ISimSystem, ISettlingSystem
{
    public string Name => "Дипломатия";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Daily;

    public void Run(World world)
    {
        if (world.Date.Day == 1)
            DiplomacyModel.Update(world);
        else
            DiplomacyModel.Sync(world);
    }

    public void Settle(World world) => DiplomacyModel.Sync(world);
}
