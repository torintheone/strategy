using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Переговоры о коалиции в стране игрока: после выборов у партии главы нет большинства. Каждая партия называет
/// условия: посты по своим местам и закон, которого хотят её избиратели; слишком далёкие по взглядам отказываются.
/// Игрок выбирает партнёров; если не успеет к сроку — коалицию соберут без него. Обещанный закон надо принять
/// за PromiseYears лет, иначе партнёр выйдет из коалиции. MODEL.md, «Выборы».
/// </summary>
public static class CoalitionTalksModel
{
    public static void Start(World world, Country country, Party head, IReadOnlyList<int> seats)
    {
        var b = world.Data.PoliticsBalance;
        var p = world.Politics![country.Id];
        var talks = new CoalitionTalks { Date = world.Date, Deadline = world.Date.AddMonths(b.TalksMonths) };
        for (var i = 0; i < p.Parties.Count; i++)
        {
            var party = p.Parties[i];
            if (party == head || seats[i] <= 0)
                continue;
            var offer = new CoalitionOffer
            {
                Party = party.Id,
                Seats = seats[i],
                Refuses = PoliticalViews.Distance(party.Position, head.Position, b.Salience) > b.CoalitionRefuse,
            };
            (offer.Law, offer.Variant) = Demand(world, country, party);
            talks.Offers.Add(offer);
        }
        p.Talks = talks;
        p.Version++;
        world.Log(country.Id, "coalition", $"У вашей партии нет большинства. Соберите коалицию до {Text.Fmt.Date(talks.Deadline)} — " +
                                           "иначе её соберут без вас (экран «Политика»).");
    }

    /// <summary>Закон, которого больше всего хотят избиратели партии (одобрение не ниже DemandSupport), или ничего.</summary>
    public static (string? Law, string? Variant) Demand(World world, Country country, Party party)
    {
        var data = world.Data;
        var laws = world.Laws?.Countries.GetValueOrDefault(country.Id);
        if (laws == null)
            return (null, null);
        (string? Law, string? Variant, double Support) best = (null, null, data.PoliticsBalance.DemandSupport);
        foreach (var law in data.Laws.Laws.Where(l => !l.Constitutional))
        {
            var current = laws.Current[law.Id];
            foreach (var v in law.Variants.Where(v => v.Id != current))
            {
                var support = Legislation.PartySupport(data, law, current, v.Id, party.Voters);
                if (support > best.Support)
                    best = (law.Id, v.Id, support);
            }
        }
        return (best.Law, best.Variant);
    }

    /// <summary>Игрок выбрал партнёров: коалиция складывается, их требования становятся обещаниями.</summary>
    public static bool Accept(World world, string countryId, IReadOnlyCollection<string> partners)
    {
        var p = world.Politics?.Countries.GetValueOrDefault(countryId);
        if (p?.Talks is not { } talks || partners.Any(id => talks.Offers.FirstOrDefault(o => o.Party == id) is not { Refuses: false }))
            return false;
        var head = PoliticsModel.HeadParty(world, countryId)!;
        var country = world[countryId];
        Cabinet.Form(world, country, partners.Append(head.Id).ToList(), head, null);
        var years = world.Data.PoliticsBalance.PromiseYears;
        foreach (var offer in talks.Offers.Where(o => partners.Contains(o.Party) && o.Law != null))
            p.Promises.Add(new CoalitionPromise { Party = offer.Party, Law = offer.Law!, Variant = offer.Variant!, Deadline = world.Date.AddYears(years) });
        p.Talks = null;
        p.Version++;
        var names = partners.Select(id => $"{Text.Fmt.Quote(p.Party(id)!.Name)}");
        var g = world.Government![countryId];
        world.Log(countryId, "coalition", partners.Count == 0
            ? $"Правительство меньшинства: {Text.Fmt.Quote(head.Name)} правит одна, у неё {g.Parliament.RulingSeats} мест из {g.Parliament.Seats}."
            : $"Коалиция сложилась: {Text.Fmt.Quote(head.Name)} и {string.Join(", ", names)} — {g.Parliament.RulingSeats} мест из {g.Parliament.Seats}.");
        return true;
    }

    /// <summary>Срок вышел: коалицию собирают без игрока (как у всех стран), из тех, кто не отказывается.</summary>
    public static void Expire(World world, Country country)
    {
        var p = world.Politics![country.Id];
        if (p.Talks is not { } talks)
            return;
        var head = PoliticsModel.HeadParty(world, country.Id)!;
        var parties = p.Parties;
        var seats = parties.Select(x => world.Government![country.Id].Parliament.Factions.FirstOrDefault(f => f.Party == x.Id)?.Seats ?? 0).ToArray();
        var refusing = Enumerable.Range(0, parties.Count).Where(i => talks.Offers.Any(o => o.Party == parties[i].Id && o.Refuses)).ToList();
        var ruling = Coalition.Form(parties, seats, world.Data.PoliticsBalance, parties.IndexOf(head), refusing);
        Accept(world, country.Id, ruling.Where(i => parties[i] != head).Select(i => parties[i].Id).ToList());
    }

    /// <summary>Обещания партнёрам: выполненные снимаются, просроченные — партнёр выходит из коалиции.</summary>
    public static void CheckPromises(World world, Country country)
    {
        var p = world.Politics![country.Id];
        var laws = world.Laws?.Countries.GetValueOrDefault(country.Id);
        foreach (var promise in p.Promises.ToList())
        {
            var party = p.Party(promise.Party);
            var law = world.Data.Law(promise.Law);
            if (party is not { Ruling: true })
                p.Promises.Remove(promise);
            else if (laws?.Current[promise.Law] == promise.Variant)
            {
                p.Promises.Remove(promise);
                world.Log(country.Id, "coalition", $"Обещание {Text.Fmt.Quote(party.Name)} выполнено: закон «{law.Name}: {law.Variant(promise.Variant).Name.ToLowerInvariant()}» принят.");
            }
            else if (world.Date > promise.Deadline)
            {
                p.Promises.Remove(promise);
                var head = PoliticsModel.HeadParty(world, country.Id)!;
                var ruling = p.Parties.Where(x => x.Ruling && x != party).Select(x => x.Id).ToList();
                Cabinet.Form(world, country, ruling, head, null);
                world.Log(country.Id, "coalition", $"{Text.Fmt.Quote(party.Name)} выходит из коалиции: закон «{law.Name}: " +
                                                   $"{law.Variant(promise.Variant).Name.ToLowerInvariant()}» так и не принят.");
            }
        }
    }
}
