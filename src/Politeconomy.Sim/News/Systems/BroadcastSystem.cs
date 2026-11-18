using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.News.Systems;

/// <summary>Телевыпуск для страны игрока: каждый понедельник, после новостей дня. MODEL.md, «Телевыпуск».</summary>
public sealed class BroadcastSystem : ISimSystem
{
    public string Name => "Телевыпуск";
    public SimPhase Phase => SimPhase.Events;
    public SimFrequency Frequency => SimFrequency.Daily;

    public void Run(World world) => BroadcastModel.Day(world);
}
