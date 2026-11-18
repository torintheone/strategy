using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics.Systems;

/// <summary>Месяц: смена власти без выборов — вотум, импичмент, перевороты, революция, передача власти хунтой. MODEL.md, «Смена власти без выборов».</summary>
public sealed class PowerSystem : ISimSystem
{
    public string Name => "Смена власти";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var politics = world.Politics ?? throw new InvalidOperationException("Политика не создана: вызовите PoliticsModel.Setup");
        foreach (var country in world.Countries)
            PowerModel.Update(world, country, politics[country.Id]);
    }
}
