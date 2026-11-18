using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.News.Systems;

/// <summary>Газета для страны игрока: 1-го числа, после новостей месяца. MODEL.md, «Газета».</summary>
public sealed class NewspaperSystem : ISimSystem
{
    public string Name => "Газета";
    public SimPhase Phase => SimPhase.Events;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world) => NewspaperModel.Month(world);
}
