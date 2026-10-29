using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics.Systems;

/// <summary>Месяц кампаний: прибавки затухают, партии агитируют, у игрока — штаб и дебаты; сроки обещаний. MODEL.md, «Кампания».</summary>
public sealed class CampaignSystem : ISimSystem
{
    public string Name => "Кампании";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var politics = world.Politics ?? throw new InvalidOperationException("Политика не создана: вызовите PoliticsModel.Setup");
        foreach (var country in world.Countries)
        {
            var p = politics[country.Id];
            if (p.Parties.Count > 0)
                CampaignModel.Update(world, country, p);
        }
    }
}
