using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.News.Systems;

/// <summary>Новости о показателях и мировых ценах: раз в месяц, после всех расчётов. MODEL.md, «Новости».</summary>
public sealed class NewsSystem : ISimSystem
{
    public string Name => "Новости";
    public SimPhase Phase => SimPhase.Events;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world) => NewsModel.Month(world);
}
