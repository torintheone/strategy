using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics.Systems;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Политика: взгляды классов, партии, опросы, фракции парламента и партийность правительства. На старте
/// парламент демократии распределяется по первому опросу (избирательная система, барьер), правит коалиция
/// крупнейшей партии; при гибридном и авторитарном режиме партия власти получает места правящего блока.
/// MODEL.md, «Взгляды», «Партии», «Опросы».
/// </summary>
public static class PoliticsModel
{
    public static IReadOnlyList<ISimSystem> Systems() => [new ViewsSystem(), new PollsSystem(), new CampaignSystem(), new ElectionSystem()];

    public static void Setup(World world)
    {
        var data = world.Data;
        var b = data.PoliticsBalance;
        var politics = WorldPolitics.Create(Rng(world).NextUInt64());
        world.Politics = politics;
        foreach (var country in world.Countries)
        {
            var s = world.Society![country.Id];
            var g = world.Government![country.Id];
            var p = new CountryPolitics { Offset = PoliticalViews.CountryOffset(country, b, Rng(world)) };
            p.Views = data.Classes.Classes.Select((c, k) => PoliticalViews.Target(c, p.Offset, s.Satisfaction[k], s.Radicalism[k], b)).ToArray();
            politics.Countries[country.Id] = p;
            p.Parties = PartyFactory.Create(world, country, p, PoliticalViews.Mean(p.Views, s));
            if (p.Parties.Count == 0)
                continue;
            var poll = Polls.Compute(b, country, p, s, g, Majoritarian(world, country), world.Date,
                Polls.EconomicVote(b, world.Economy![country.Id]));
            for (var i = 0; i < p.Parties.Count; i++)
                (p.Parties[i].Poll, p.Parties[i].Voters) = (poll.Shares[i], poll.Voters[i]);
            FormParliament(world, country, p, g);
            // Опрос ещё раз: теперь известно, кто правит (это влияет на привлекательность).
            PollsSystem.Update(world, country, p, s, g, months: 0);
            Backfill(world, p, b);
            Elections.Schedule(world, country, p);
            Elections.RecordStart(world, country, p);
            g.Leader.Terms = StartTerms(world, country);
        }
    }

    /// <summary>
    /// Сколько сроков глава уже отслужил на старте: если сроков два, с вероятностью SecondTermShare он на втором
    /// (и на следующих выборах партия выдвинет преемника), иначе на первом.
    /// </summary>
    static int StartTerms(World world, Country country)
    {
        var law = world.Data.Laws.Laws.FirstOrDefault(l => l.Id == "term_limits");
        var limit = law == null ? null : Laws.LawModel.StartVariant(law, country);
        return limit == "two" && Rng(world).Chance(world.Data.PoliticsBalance.SecondTermShare) ? 2 : 1;
    }

    /// <summary>
    /// Опросы за год до старта: случайное блуждание назад от стартовых долей (по BackfillDrift в месяц),
    /// чтобы график опросов с первого дня показывал движение, а не одну точку.
    /// </summary>
    static void Backfill(World world, CountryPolitics p, Data.PoliticsBalance b)
    {
        var shares = new Dictionary<string, double>(p.Polls[^1].Shares, StringComparer.Ordinal);
        for (var m = 1; m <= b.PollBackfill; m++)
        {
            foreach (var id in shares.Keys.ToList())
                shares[id] *= Math.Exp(Rng(world).NextGaussian() * b.BackfillDrift);
            var total = shares.Values.Sum();
            p.Polls.Insert(0, new PollRecord(world.Date.AddMonths(-m), shares.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / total, 4), StringComparer.Ordinal)));
        }
    }

    /// <summary>Места в парламенте по первому опросу, правящая коалиция, глава и министры по партиям.</summary>
    static void FormParliament(World world, Country country, CountryPolitics p, CountryGovernment g)
    {
        var data = world.Data;
        var b = data.PoliticsBalance;
        var record = data.PoliticsOf(country.Id);
        var parliament = g.Parliament;
        var parties = p.Parties;
        var seats = new int[parties.Count];
        List<int> ruling;
        if (country.Regime == Regime.Democracy)
        {
            if (parliament.Exists)
                seats = Seats.Allocate(record.Electoral, parties.Select(x => x.Poll).ToArray(), parliament.Seats, record.Threshold, b.MajoritarianPower);
            else
                seats = Seats.LargestRemainder(parties.Select(x => x.Poll).ToArray(), 100);
            // В президентской республике главный соперник в коалицию не идёт: власть может быть «разделённой».
            var largest = Enumerable.Range(0, parties.Count).OrderByDescending(i => seats[i]).ThenByDescending(i => parties[i].Poll).First();
            ruling = Coalition.Form(parties, seats, b, largest, Elections.HasPresident(world, country) ? Coalition.Rival(seats, largest) : null);
        }
        else
        {
            // Партия власти получает места правящего блока, остальные делят оставшиеся по опросу.
            var powerSeats = !parliament.Exists ? 0 : parties.Count == 1 ? parliament.Seats : parliament.RulingSeats;
            var rest = Seats.LargestRemainder(parties.Skip(1).Select(x => x.Poll).ToArray(), Math.Max(0, parliament.Seats - powerSeats));
            seats = [powerSeats, .. rest];
            ruling = [0];
        }
        // Правительство у власти уже несколько лет (партия власти — давно).
        var years = country.Regime == Regime.Democracy ? Rng(world).Next(0, b.StartTenure + 1) : Rng(world).Next(b.StartTenure, 3 * b.StartTenure);
        foreach (var i in ruling)
            (parties[i].Ruling, parties[i].RulingSince) = (true, world.Date.AddYears(-years));
        if (parliament.Exists)
            parliament.Factions = Enumerable.Range(0, parties.Count)
                .Where(i => seats[i] > 0)
                .OrderByDescending(i => parties[i].Ruling).ThenByDescending(i => seats[i])
                .Select(i => new Faction { Party = parties[i].Id, Seats = seats[i], Ruling = parties[i].Ruling })
                .ToList();

        // Глава исполнительной власти — лидер крупнейшей партии коалиции.
        var head = parties[ruling.OrderByDescending(i => seats[i]).First()];
        g.Leader = head.Leader!;
        g.Leader.Since = world.Date;
        head.Leader = null;
        var quota = ruling.ToDictionary(i => parties[i].Id, i => (double)Math.Max(1, seats[i]));
        AssignMinisters(world, country, g, quota);
        g.Version++;
    }

    /// <summary>
    /// Министры по партиям коалиции: посты делятся пропорционально местам. Технократы и силовики при несвободных
    /// режимах часто беспартийные.
    /// </summary>
    static void AssignMinisters(World world, Country country, CountryGovernment g, Dictionary<string, double> quota)
    {
        var p = world.Politics![country.Id];
        var total = quota.Values.Sum();
        var posts = g.Ministers.Count;
        var assigned = quota.Keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        foreach (var (_, minister) in g.Ministers)
        {
            if (NonPartisan(world, country, minister))
            {
                minister.Party = null;
                continue;
            }
            var party = quota.Keys.OrderByDescending(k => quota[k] / total * posts - assigned[k]).First();
            assigned[party]++;
            PartyFactory.Align(world, minister, p.Party(party)!);
        }
    }

    static bool NonPartisan(World world, Country country, Character c)
    {
        var chance = country.Regime == Regime.Democracy ? 0.25 : 0.5;
        return (c.Has("technocrat") || c.Has("silovik")) && Rng(world).Chance(chance);
    }

    /// <summary>
    /// Новый министр или кандидат: из той же партии, что и прежний министр (если она ещё правит), иначе из
    /// правящей партии; технократ может остаться беспартийным.
    /// </summary>
    public static void Affiliate(World world, string countryId, Character c, string? previous)
    {
        var p = world.Politics?.Countries.GetValueOrDefault(countryId);
        if (p == null || p.Parties.Count == 0)
            return;
        var country = world[countryId];
        if (NonPartisan(world, country, c))
            return;
        var party = p.Party(previous) is { Ruling: true } same ? same : HeadParty(world, countryId);
        if (party != null)
            PartyFactory.Align(world, c, party);
    }

    /// <summary>Генератор случайных чисел политики (свой, чтобы не сбивать экономику; до создания политики — общий).</summary>
    public static DeterministicRandom Rng(World world) => world.Politics?.Random ?? world.Random;

    /// <summary>Скандал после раскрытых фальсификаций (0 — нет; растёт с масштабом, затухает со временем).</summary>
    public static double Scandal(World world, string countryId) => world.Politics?.Countries.GetValueOrDefault(countryId)?.Scandal ?? 0;

    /// <summary>Мажоритарная система: голос за мелкую партию «теряется», и её поддерживают меньше.</summary>
    public static bool Majoritarian(World world, Country country) =>
        country.Regime == Regime.Democracy && Elections.Electoral(world, country) == "majoritarian";

    /// <summary>Партия главы исполнительной власти.</summary>
    public static Party? HeadParty(World world, string countryId) =>
        world.Politics?.Countries.GetValueOrDefault(countryId)?.Party(world.Government?.Countries.GetValueOrDefault(countryId)?.Leader.Party);

    /// <summary>Лидер партии: свой или, для партии главы, сам глава.</summary>
    public static Character LeaderOf(World world, string countryId, Party party) => party.Leader ?? world.Government![countryId].Leader;

    /// <summary>Из кого состоят избиратели партии (null — партии нет).</summary>
    public static IReadOnlyList<double>? Voters(World world, string countryId, string? party) =>
        world.Politics?.Countries.GetValueOrDefault(countryId)?.Party(party)?.Voters;

    /// <summary>Одобрение проекта избирателями: 0,5 + 0,5·th(Σ доля класса среди избирателей × его отношение / scale).</summary>
    public static double Support(IReadOnlyList<double> voters, IReadOnlyList<double> attitudes, double scale)
    {
        var shift = 0.0;
        for (var k = 0; k < voters.Count && k < attitudes.Count; k++)
            shift += voters[k] * attitudes[k];
        return 0.5 + 0.5 * Math.Tanh(shift / scale);
    }
}
