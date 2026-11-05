using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Science.Systems;

/// <summary>Месяц науки: очки исследований, освоение технологий, распространение по миру, поправки. MODEL.md, «Наука».</summary>
public sealed class ScienceSystem : ISimSystem
{
    public string Name => "Наука";
    public SimPhase Phase => SimPhase.Economy;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var science = world.Science ?? throw new InvalidOperationException("Наука не создана: вызовите ScienceModel.Setup");
        foreach (var country in world.Countries)
            ScienceModel.Update(world, country, science[country.Id]);
        ScienceModel.Spread(world);
        foreach (var country in world.Countries)
            ScienceModel.Apply(world, country, science[country.Id]);
    }
}
