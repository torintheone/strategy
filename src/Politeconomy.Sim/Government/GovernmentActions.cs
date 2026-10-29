using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Government;

/// <summary>Решения игрока о кадрах. Вызываются в потоке симуляции (через SimulationHost.Post).</summary>
public static class GovernmentActions
{
    /// <summary>Подобрать кандидатов на пост: несколько новых людей, из которых можно выбрать замену министру.</summary>
    public static void RequestCandidates(World world, string countryId, string postId)
    {
        var government = world.Government ?? throw new InvalidOperationException("Правительства не созданы");
        var post = world.Data.Government.Posts.First(p => p.Id == postId);
        var country = world[countryId];
        var list = new List<Character>();
        var g = government[countryId];
        for (var i = 0; i < world.Data.GovernmentBalance.Candidates; i++)
        {
            var candidate = CharacterFactory.Create(world, country, post, leader: false);
            Politics.PoliticsModel.Affiliate(world, countryId, candidate, g.Minister(postId)?.Party);
            list.Add(candidate);
        }
        g.Candidates[postId] = list;
        g.Version++;
    }

    /// <summary>Назначить на пост кандидата с номером index; прежний министр уходит.</summary>
    public static bool Appoint(World world, string countryId, string postId, int index)
    {
        var g = world.Government?[countryId];
        if (g == null || !g.Candidates.TryGetValue(postId, out var list) || index < 0 || index >= list.Count)
            return false;
        var post = world.Data.Government.Posts.First(p => p.Id == postId);
        var chosen = list[index];
        var old = g.Ministers[postId];
        chosen.Since = world.Date;
        chosen.Loyalty = Math.Min(99, chosen.Loyalty + world.Data.GovernmentBalance.AppointmentLoyalty);
        g.Ministers[postId] = chosen;
        g.Candidates.Remove(postId);
        g.Version++;
        world.Log(countryId, "appointment", $"{post.Name} {old.Name} отправлен{(old.Female ? "а" : "")} в отставку. Новый человек на посту — {chosen.Name}.");
        return true;
    }
}
