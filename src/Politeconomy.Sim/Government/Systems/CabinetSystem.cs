using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Government.Systems;

/// <summary>
/// Месяц жизни правительств: министры стареют, пожилые уходят на пенсию, изредка кто-то уходит в отставку
/// по своим причинам. На освободившийся пост назначается новый человек. MODEL.md, «Правительство».
/// </summary>
public sealed class CabinetSystem : ISimSystem
{
    public string Name => "Правительство";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var government = world.Government ?? throw new InvalidOperationException("Правительства не созданы: вызовите GovernmentModel.Setup");
        var data = world.Data;
        var b = data.GovernmentBalance;
        foreach (var country in world.Countries)
        {
            var g = government[country.Id];
            foreach (var post in data.Government.Posts)
            {
                var minister = g.Ministers[post.Id];
                var age = minister.Age(world.Date);
                var retire = age > b.RetirementAge && world.Random.Chance(b.RetirementRate * (age - b.RetirementAge));
                if (!retire && !world.Random.Chance(b.ResignationRate))
                    continue;
                var successor = CharacterFactory.Create(world, country, post, leader: false);
                g.Ministers[post.Id] = successor;
                g.Candidates.Remove(post.Id);
                g.Version++;
                var reason = retire ? "ушёл на пенсию" : "ушёл в отставку";
                if (minister.Female)
                    reason = retire ? "ушла на пенсию" : "ушла в отставку";
                world.Log(country.Id, retire ? "retirement" : "resignation",
                    $"{post.Name} {minister.Name} {reason}. На пост назначен{(successor.Female ? "а" : "")} {successor.Name}.");
            }
        }
    }
}
