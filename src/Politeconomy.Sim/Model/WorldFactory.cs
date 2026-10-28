using Politeconomy.Sim.Data;

namespace Politeconomy.Sim.Model;

/// <summary>Создаёт стартовое состояние мира из данных и сценария.</summary>
public static class WorldFactory
{
    public static World Create(GameData data, ulong? seed = null)
    {
        var politics = data.Politics.Countries.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var countries = new List<Country>();
        foreach (var record in data.Countries.Countries.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            var country = new Country
            {
                Id = record.Id,
                Info = record,
                System = politics.TryGetValue(record.Id, out var p) ? ParseSystem(p.System) : PoliticalSystem.PresidentialRepublic,
                Income = ParseIncome(record.IncomeGroup),
            };
            foreach (var (key, s) in record.Stats)
                country.Stats[key] = new StatValue(s.Value, s.Year, s.Source, s.Method, s.N);
            countries.Add(country);
        }

        var control = data.Control.Control.ToDictionary(c => c.Region, c => c.Controller, StringComparer.Ordinal);
        var regions = data.Regions.Regions
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new Region { Id = r.Id, Info = r, Owner = r.Country, Controller = control.GetValueOrDefault(r.Id, r.Country) })
            .ToList();

        var actualSeed = seed ?? data.Scenario.Seed;
        return new World
        {
            Data = data,
            Date = data.Scenario.StartDate,
            Seed = actualSeed,
            Random = new DeterministicRandom(actualSeed),
            PlayerCountry = data.Scenario.PlayerCountry,
            Countries = countries,
            Regions = regions,
        };
    }

    public static PoliticalSystem ParseSystem(string id) => id switch
    {
        "presidential_republic" => PoliticalSystem.PresidentialRepublic,
        "parliamentary_republic" => PoliticalSystem.ParliamentaryRepublic,
        "semi_presidential" => PoliticalSystem.SemiPresidential,
        "constitutional_monarchy" => PoliticalSystem.ConstitutionalMonarchy,
        "absolute_monarchy" => PoliticalSystem.AbsoluteMonarchy,
        "one_party_state" => PoliticalSystem.OnePartyState,
        "military_regime" => PoliticalSystem.MilitaryRegime,
        _ => throw new DataException($"Неизвестный тип политической системы «{id}»"),
    };

    public static string SystemId(PoliticalSystem system) => system switch
    {
        PoliticalSystem.PresidentialRepublic => "presidential_republic",
        PoliticalSystem.ParliamentaryRepublic => "parliamentary_republic",
        PoliticalSystem.SemiPresidential => "semi_presidential",
        PoliticalSystem.ConstitutionalMonarchy => "constitutional_monarchy",
        PoliticalSystem.AbsoluteMonarchy => "absolute_monarchy",
        PoliticalSystem.OnePartyState => "one_party_state",
        PoliticalSystem.MilitaryRegime => "military_regime",
        _ => throw new ArgumentOutOfRangeException(nameof(system)),
    };

    public static IncomeGroup ParseIncome(string id) => id switch
    {
        "low" => IncomeGroup.Low,
        "lower_middle" => IncomeGroup.LowerMiddle,
        "upper_middle" => IncomeGroup.UpperMiddle,
        "high" => IncomeGroup.High,
        _ => throw new DataException($"Неизвестная группа дохода «{id}»"),
    };

    public static string IncomeId(IncomeGroup group) => group switch
    {
        IncomeGroup.Low => "low",
        IncomeGroup.LowerMiddle => "lower_middle",
        IncomeGroup.UpperMiddle => "upper_middle",
        IncomeGroup.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(group)),
    };
}
