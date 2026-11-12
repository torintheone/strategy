using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Events.Systems;

/// <summary>События: каждый день — сроки решений игрока, в дни событий — новые события и продолжения цепочек. MODEL.md, «События».</summary>
public sealed class EventSystem : ISimSystem
{
    public string Name => "События";
    public SimPhase Phase => SimPhase.Events;
    public SimFrequency Frequency => SimFrequency.Daily;

    public void Run(World world) => EventModel.Day(world);
}
