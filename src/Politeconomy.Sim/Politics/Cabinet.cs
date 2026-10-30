using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Правительство после выборов или переговоров: какие партии правят, кто глава, кто министры. Посты делятся между
/// партиями коалиции пропорционально местам; лишние министры партии уступают посты тем, кому их не хватает.
/// При смене партии главы всё правительство новое. MODEL.md, «Выборы».
/// </summary>
public static class Cabinet
{
    /// <summary>
    /// Правящие партии ruling, партия главы headParty. NewHead — новый глава (null — прежний остаётся).
    /// Successor — преемник прежнего главы, которому нельзя было избираться: прежний глава уходит из политики.
    /// </summary>
    public static void Form(World world, Country country, IReadOnlyCollection<string> ruling, Party headParty, Character? newHead,
        Character? successor = null)
    {
        var p = world.Politics![country.Id];
        var g = world.Government![country.Id];
        var oldHead = g.Leader;
        var oldParty = p.Party(oldHead.Party);
        foreach (var party in p.Parties)
        {
            var now = ruling.Contains(party.Id);
            if (now && !party.Ruling)
                party.RulingSince = world.Date;
            if (!now)
                party.RulingSince = null;
            party.Ruling = now;
        }
        foreach (var f in g.Parliament.Factions)
            f.Ruling = f.Party != null && ruling.Contains(f.Party);

        var partyChanged = oldParty != headParty;
        if (newHead != null && newHead != oldHead)
        {
            if (successor == null && oldParty != null && partyChanged)
                oldParty.Leader = oldHead;
            headParty.Leader = null;
            newHead.Party = headParty.Id;
            newHead.Since = world.Date;
            g.Leader = newHead;
            if (!partyChanged)
                world.Log(country.Id, "leader", $"{oldHead.Name} уходит: срок истёк. Страну возглавляет преемник — {newHead.Name} ({Text.Fmt.Quote(headParty.Name)}).");
            else if (world.PlayerCountry == country.Id)
                world.Defeat = new Defeat(world.Date, country.Id, oldHead.Name, newHead.Name, headParty.Name);
        }
        Rebalance(world, country, partyChanged);
        p.Formed = world.Date;
        g.Version++;
        p.Version++;
    }

    /// <summary>
    /// Министры по партиям коалиции. Fresh — новое правительство целиком (сменилась партия главы). Иначе меняются
    /// только министры партий, ушедших из коалиции, и лишние министры партий, у которых постов больше квоты.
    /// </summary>
    public static void Rebalance(World world, Country country, bool fresh)
    {
        var p = world.Politics![country.Id];
        var g = world.Government![country.Id];
        var posts = world.Data.Government.Posts;
        var seats = g.Parliament.Factions.Where(f => f.Ruling && f.Party != null).ToDictionary(f => f.Party!, f => (double)Math.Max(1, f.Seats));
        var head = p.Party(g.Leader.Party);
        if (head != null)
            seats.TryAdd(head.Id, 1);
        if (seats.Count == 0)
            return;
        var total = seats.Values.Sum();
        var partisan = fresh ? posts.Count : g.Ministers.Values.Count(m => m.Party != null);
        var quota = seats.ToDictionary(kv => kv.Key, kv => kv.Value / total * partisan);
        var held = seats.Keys.ToDictionary(k => k, k => fresh ? 0 : g.Ministers.Values.Count(m => m.Party == k), StringComparer.Ordinal);

        foreach (var post in posts)
        {
            var minister = g.Ministers[post.Id];
            var keep = !fresh && (minister.Party == null || seats.ContainsKey(minister.Party) && held[minister.Party] <= Math.Ceiling(quota[minister.Party]));
            if (keep)
                continue;
            if (minister.Party != null && held.ContainsKey(minister.Party))
                held[minister.Party]--;
            var party = quota.Keys.OrderByDescending(k => quota[k] - held[k]).First();
            var successor = CharacterFactory.Create(world, country, post, leader: false, PoliticsModel.Rng(world));
            PartyFactory.Align(world, successor, p.Party(party)!);
            held[party]++;
            g.Ministers[post.Id] = successor;
            g.Candidates.Remove(post.Id);
        }
    }
}
