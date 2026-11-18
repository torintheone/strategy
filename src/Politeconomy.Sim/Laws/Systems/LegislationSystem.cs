using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Laws.Systems;

/// <summary>Каждый месяц готовые законопроекты выносятся на голосование. MODEL.md, «Законы».</summary>
public sealed class LegislationSystem : ISimSystem
{
    public string Name => "Законопроекты";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var laws = world.Laws ?? throw new InvalidOperationException("Законы не созданы: вызовите LawModel.Setup");
        foreach (var country in world.Countries)
        {
            var c = laws[country.Id];
            foreach (var bill in c.Bills.Where(b => b.Stage == "drafting" && b.Ready <= world.Date).ToList())
                Legislation.Decide(world, country.Id, c, bill);
        }
    }
}
