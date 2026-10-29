using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Government;

/// <summary>
/// Создаёт вымышленных персонажей: имя по культуре страны (data/names), пол, возраст, качества и черты.
/// Реальных политиков в игре нет. MODEL.md, «Правительство».
/// </summary>
public static class CharacterFactory
{
    /// <summary>Random — чей генератор брать (по умолчанию общий генератор мира).</summary>
    public static Character Create(World world, Country country, PostRecord? post, bool leader, DeterministicRandom? random = null)
    {
        var data = world.Data;
        var b = data.GovernmentBalance;
        var r = random ?? world.Random;
        var culture = CultureOf(data, country.Id);
        var income = (int)country.Income;
        var female = r.Chance(leader ? b.FemaleLeaderShare : Pick(b.FemaleShare, income));
        var ages = leader ? b.LeaderAge : b.MinisterAge;
        var c = new Character
        {
            Id = NextId(world),
            Female = female,
            Culture = culture.Id,
            Name = Name(culture, female, r),
            BirthYear = world.Date.Year - r.Next(ages[0], ages[1] + 1),
            Competence = Quality(r, Pick(b.CompetenceMean, income), b.CompetenceSpread),
            Loyalty = Quality(r, b.LoyaltyMean, b.LoyaltySpread),
            Corruption = Quality(r, Pick(b.CorruptionMean, income), b.CorruptionSpread),
            Popularity = Quality(r, b.PopularityMean, b.PopularitySpread),
            Since = world.Date,
        };
        for (var axis = 0; axis < c.Views.Length; axis++)
            c.Views[axis] = Math.Clamp(r.NextGaussian() * b.ViewsSpread, -100, 100);

        var traits = data.Government.Traits;
        if (r.Chance(b.FirstTrait))
            AddTrait(c, PickTrait(traits, c, post, b, r), data);
        if (c.Traits.Count > 0 && r.Chance(b.SecondTrait))
            AddTrait(c, PickTrait(traits, c, post, b, r), data);
        return c;
    }

    static int NextId(World world) => world.Government is { } g ? g.NextId++ : 0;

    public static CultureRecord CultureOf(GameData data, string country) =>
        data.Cultures.FirstOrDefault(c => c.Countries.Contains(country))
        ?? throw new DataException($"Нет культуры имён для страны {country} (data/names)");

    static double Pick(IReadOnlyList<double> values, int index) => values[Math.Clamp(index, 0, values.Count - 1)];

    static double Quality(DeterministicRandom r, double mean, double spread) => Math.Clamp(mean + r.NextGaussian() * spread, 1, 99);

    static string Name(CultureRecord culture, bool female, DeterministicRandom r)
    {
        var given = female ? culture.Female : culture.Male;
        var first = given[r.Next(0, given.Count)];
        var family = culture.Surnames[r.Next(0, culture.Surnames.Count)];
        if (female)
            family = Feminine(culture, family);
        return culture.Order == "family_given" ? $"{family} {first}" : $"{first} {family}";
    }

    /// <summary>Женская форма фамилии: самое длинное подходящее окончание из правил культуры.</summary>
    public static string Feminine(CultureRecord culture, string surname)
    {
        IReadOnlyList<string>? best = null;
        foreach (var rule in culture.Feminine)
            if (surname.EndsWith(rule[0], StringComparison.Ordinal) && (best == null || rule[0].Length > best[0].Length))
                best = rule;
        return best == null ? surname : surname[..^best[0].Length] + best[1];
    }

    static TraitRecord PickTrait(IReadOnlyList<TraitRecord> traits, Character c, PostRecord? post, GovernmentBalance b, DeterministicRandom r)
    {
        double Weight(TraitRecord t)
        {
            if (c.Traits.Contains(t.Id))
                return 0;
            var w = t.Weight;
            if (post is { Security: true } && t.Id == "silovik")
                w *= b.SecurityTraitBoost;
            if (post is { Finance: true } && t.Id == "technocrat")
                w *= b.FinanceTraitBoost;
            return w;
        }
        var total = traits.Sum(Weight);
        var x = r.NextDouble() * total;
        foreach (var t in traits)
        {
            x -= Weight(t);
            if (x < 0)
                return t;
        }
        return traits[^1];
    }

    static void AddTrait(Character c, TraitRecord t, GameData data)
    {
        if (c.Traits.Contains(t.Id))
            return;
        c.Traits.Add(t.Id);
        c.Competence = Math.Clamp(c.Competence + t.Competence, 1, 99);
        c.Loyalty = Math.Clamp(c.Loyalty + t.Loyalty, 1, 99);
        c.Corruption = Math.Clamp(c.Corruption + t.Corruption, 1, 99);
        c.Popularity = Math.Clamp(c.Popularity + t.Popularity, 1, 99);
    }
}
