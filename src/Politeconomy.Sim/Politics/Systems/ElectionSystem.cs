using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics.Systems;

/// <summary>
/// 1-го числа: если подошла дата — выборы президента, затем парламента, и конец кампании; переговоры о коалиции,
/// у которых вышел срок; обещания партнёрам по коалиции. MODEL.md, «Выборы».
/// </summary>
public sealed class ElectionSystem : ISimSystem
{
    public string Name => "Выборы";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var politics = world.Politics ?? throw new InvalidOperationException("Политика не создана: вызовите PoliticsModel.Setup");
        foreach (var country in world.Countries)
        {
            var p = politics[country.Id];
            if (p.Parties.Count == 0)
                continue;
            if (p.Talks is { } talks && world.Date >= talks.Deadline)
                CoalitionTalksModel.Expire(world, country);
            if (p.Promises.Count > 0)
                CoalitionTalksModel.CheckPromises(world, country);
            if (p.NextPresident is { } president && world.Date >= president)
                Elections.HoldPresident(world, country);
            if (p.NextElection is { } election && world.Date >= election && world.Government![country.Id].Parliament.Exists)
                Elections.HoldParliament(world, country);
            if (p.Campaign is { } campaign && world.Date >= campaign.Election)
                CampaignModel.End(world, country, p);
        }
    }
}
