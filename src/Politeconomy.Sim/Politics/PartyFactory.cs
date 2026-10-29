using Politeconomy.Sim.Data;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Партии страны на старте. Сколько их — по избирательной системе (демократия) или по режиму. В демократии всегда
/// есть крупная партия левее центра и крупная правее, остальные семейства выбираются по весам. В гибридном и
/// авторитарном режиме первой идёт партия власти, при авторитарном остальные — лояльная оппозиция.
/// Позиция партии отсчитывается от средних взглядов страны. MODEL.md, «Партии».
/// </summary>
public static class PartyFactory
{
    static readonly (string Id, double Weight)[] LeftBig = [("social_democrat", 1), ("socialist", 0.3), ("populist", 0.2)];
    static readonly (string Id, double Weight)[] RightBig = [("conservative", 1), ("nationalist", 0.2), ("religious", 0.2)];

    public static List<Party> Create(World world, Country country, CountryPolitics politics, double[] mean)
    {
        var data = world.Data;
        var record = data.PoliticsOf(country.Id);
        if (!record.Parties)
            return [];
        var b = data.PoliticsBalance;
        var r = world.Random;
        var democracy = country.Regime == Regime.Democracy;
        var range = country.Regime switch
        {
            Regime.Hybrid => b.HybridParties,
            Regime.Authoritarian => b.AuthoritarianParties,
            _ => record.Electoral switch
            {
                "majoritarian" => b.MajoritarianParties,
                "mixed" => b.MixedParties,
                _ => b.ProportionalParties,
            },
        };
        var count = r.Next(range[0], range[1] + 1);

        var families = new List<string>();
        if (!democracy)
            families.Add(record.RulingFamily ?? data.Parties.Power.Id);
        else
        {
            families.Add(Pick(LeftBig, r));
            families.Add(Pick(RightBig.Where(f => !families.Contains(f.Id)).ToArray(), r));
        }
        while (families.Count < count)
        {
            var options = data.Parties.Families
                .Where(f => !families.Contains(f.Id))
                .Select(f => (f.Id, f.Weight * (f.ByIncome is { } w ? w[Math.Clamp((int)country.Income, 0, w.Count - 1)] : 1)))
                .ToArray();
            if (options.Length == 0)
                break;
            families.Add(Pick(options, r));
        }

        var center = PoliticalViews.Center(data.Classes.Classes);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var region = country.Info.Region;
        var parties = new List<Party>();
        for (var i = 0; i < families.Count; i++)
        {
            var power = !democracy && i == 0;
            var family = data.Family(families[i]);
            // Две крупные партии мажоритарной системы борются за центр: они ближе к средним взглядам,
            // но по экономике одна всегда левее среднего, другая правее.
            var big = democracy && record.Electoral == "majoritarian" && i < 2;
            var scale = big ? b.BigScale : b.FamilyScale;
            var position = new double[PoliticalViews.Axes];
            for (var a = 0; a < PoliticalViews.Axes; a++)
            {
                var shift = power ? data.Parties.Power.Position[a] : scale * (family.Position[a] - center[a]);
                position[a] = Math.Clamp(mean[a] + shift + r.NextGaussian() * b.PositionSpread, -100, 100);
            }
            if (big)
                position[0] = i == 0 ? Math.Min(position[0], mean[0] - b.BigSide) : Math.Max(position[0], mean[0] + b.BigSide);
            var (name, @short) = PartyNames.Generate(data.Parties, family, region, r, used);
            var party = new Party
            {
                Id = $"{country.Id}-{politics.NextParty++}",
                Name = name,
                Short = @short,
                Family = family.Id,
                Color = family.Color,
                Position = position,
                Organization = power ? b.PowerOrganization
                    : big ? b.BigOrganization
                    : Math.Clamp(b.OrganizationMean + r.NextGaussian() * b.OrganizationSpread, 0.1, 0.95),
                Founded = world.Date.Year - r.Next(power ? 10 : 2, power ? 60 : 50),
                Power = power,
                Loyal = country.Regime == Regime.Authoritarian && !power,
            };
            party.Leader = PartyLeader(world, country, party);
            parties.Add(party);
        }
        return parties;
    }

    /// <summary>Лидер партии: новый персонаж со взглядами партии.</summary>
    public static Character PartyLeader(World world, Country country, Party party)
    {
        var leader = CharacterFactory.Create(world, country, null, leader: true);
        Align(world, leader, party);
        return leader;
    }

    /// <summary>Персонаж вступает в партию: его взгляды становятся близки к её позиции.</summary>
    public static void Align(World world, Character c, Party party)
    {
        c.Party = party.Id;
        for (var a = 0; a < PoliticalViews.Axes; a++)
            c.Views[a] = Math.Clamp(party.Position[a] + world.Random.NextGaussian() * world.Data.PoliticsBalance.PositionSpread, -100, 100);
    }

    static string Pick(IReadOnlyList<(string Id, double Weight)> options, DeterministicRandom r)
    {
        var total = options.Sum(o => o.Weight);
        var x = r.NextDouble() * total;
        foreach (var (id, weight) in options)
        {
            x -= weight;
            if (x < 0)
                return id;
        }
        return options[^1].Id;
    }
}
