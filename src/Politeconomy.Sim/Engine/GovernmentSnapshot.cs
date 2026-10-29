using System.Runtime.CompilerServices;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Engine;

/// <summary>
/// Персонаж в снимке: возраст на дату снимка, качества 0–100, черты (id из government.json), взгляды по трём осям,
/// партия (id; null — беспартийный).
/// </summary>
public sealed record CharacterSnapshot(int Id, string Name, bool Female, int Age, double Competence, double Loyalty,
    double Corruption, double Popularity, IReadOnlyList<string> Traits, IReadOnlyList<double> Views, DateOnly Since, string? Party = null)
{
    public static CharacterSnapshot Of(Character c, DateOnly date) => new(c.Id, c.Name, c.Female, c.Age(date), c.Competence, c.Loyalty,
        c.Corruption, c.Popularity, c.Traits.ToArray(), c.Views.ToArray(), c.Since, c.Party);
}

public sealed record MinisterSnapshot(string Post, CharacterSnapshot Person);

/// <summary>Фракция в снимке: партия (null — блок без партии), места, правящая ли.</summary>
public sealed record FactionSnapshot(string? Party, int Seats, bool Ruling);

/// <summary>Парламент в снимке: вид (real, formal, none), название, места всего и у правящего блока, фракции.</summary>
public sealed record ParliamentSnapshot(string Kind, string Name, int Seats, int Ruling, double Discipline, IReadOnlyList<FactionSnapshot> Factions)
{
    public int Opposition => Seats - Ruling;
}

/// <summary>Правительство страны в снимке. Кандидаты есть только на посты, где игрок попросил замену.</summary>
public sealed record GovernmentSnapshot(
    string LeaderTitle,
    CharacterSnapshot Leader,
    IReadOnlyList<MinisterSnapshot> Ministers,
    IReadOnlyDictionary<string, IReadOnlyList<CharacterSnapshot>> Candidates,
    ParliamentSnapshot Parliament)
{
    public CharacterSnapshot? Minister(string post) => Ministers.FirstOrDefault(m => m.Post == post)?.Person;

    // Снимок правительства пересобирается, только когда меняется состав или наступает новый год (возраст).
    static readonly ConditionalWeakTable<CountryGovernment, Cached> Cache = new();

    sealed record Cached(int Version, int Year, GovernmentSnapshot Snapshot);

    public static GovernmentSnapshot Capture(World world, Country country, CountryGovernment g)
    {
        if (Cache.TryGetValue(g, out var cached) && cached.Version == g.Version && cached.Year == world.Date.Year)
            return cached.Snapshot;
        var date = world.Date;
        CharacterSnapshot Person(Character c) => CharacterSnapshot.Of(c, date);
        var p = g.Parliament;
        var snapshot = new GovernmentSnapshot(
            GovernmentModel.LeaderTitle(world.Data, country),
            Person(g.Leader),
            g.Ministers.Select(kv => new MinisterSnapshot(kv.Key, Person(kv.Value))).ToList(),
            g.Candidates.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<CharacterSnapshot>)kv.Value.Select(Person).ToList(), StringComparer.Ordinal),
            new ParliamentSnapshot(p.Kind, p.Name, p.Seats, p.RulingSeats, p.Discipline,
                p.Factions.Select(f => new FactionSnapshot(f.Party, f.Seats, f.Ruling)).ToList()));
        Cache.AddOrUpdate(g, new Cached(g.Version, world.Date.Year, snapshot));
        return snapshot;
    }
}
