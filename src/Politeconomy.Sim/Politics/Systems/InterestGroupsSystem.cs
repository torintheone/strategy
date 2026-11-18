using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics.Systems;

/// <summary>Месяц групп интересов: влияние, одобрение, требования, давление и деньги партиям. MODEL.md, «Группы интересов».</summary>
public sealed class InterestGroupsSystem : ISimSystem
{
    public string Name => "Группы интересов";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var politics = world.Politics ?? throw new InvalidOperationException("Политика не создана: вызовите PoliticsModel.Setup");
        foreach (var country in world.Countries)
        {
            var p = politics[country.Id];
            if (p.Groups.Count > 0)
                InterestGroupsModel.Update(world, country, p, months: 1);
        }
    }
}
