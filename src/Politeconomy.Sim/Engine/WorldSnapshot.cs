using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Engine;

/// <summary>
/// Неизменяемый снимок мира для интерфейса. Симуляция в фоновом потоке публикует новый снимок
/// после каждого шага, интерфейс читает последний — без блокировок и гонок.
/// </summary>
public sealed class WorldSnapshot
{
    public required DateOnly Date { get; init; }
    public required int Speed { get; init; }
    public required bool Paused { get; init; }
    public required string? PlayerCountry { get; init; }
    public required IReadOnlyDictionary<string, CountrySnapshot> Countries { get; init; }
    /// <summary>Регионы, которые контролирует не их владелец: регион → контролирующая страна.</summary>
    public required IReadOnlyDictionary<string, string> Occupied { get; init; }
    /// <summary>Последние записи журнала (отставки, назначения, голосования), новые — в конце.</summary>
    public IReadOnlyList<JournalEntry> Journal { get; init; } = [];
    /// <summary>Валюта каждой страны: код, режим курса, якорь и сколько стран ею пользуются.</summary>
    public required IReadOnlyDictionary<string, CurrencySnapshot> Currencies { get; init; }
    public required long Version { get; init; }

    /// <summary>Кто контролирует регион: оккупант или, если его нет, владелец.</summary>
    public string ControllerOf(string regionId, string owner) => Occupied.GetValueOrDefault(regionId, owner);

    const int JournalShown = 60;

    public static WorldSnapshot Capture(World world, int speed, bool paused, long version)
    {
        var countries = new Dictionary<string, CountrySnapshot>(world.Countries.Count, StringComparer.Ordinal);
        foreach (var c in world.Countries)
        {
            double[]? ages = null;
            if (world.Demography?.Countries.GetValueOrDefault(c.Id) is { } d)
                ages = [.. d.Male, .. d.Female];
            List<ClassSnapshot>? classes = null;
            if (world.Society?.Countries.GetValueOrDefault(c.Id) is { } s)
            {
                var total = s.Total;
                classes = new List<ClassSnapshot>(s.Size.Length);
                for (var k = 0; k < s.Size.Length; k++)
                    classes.Add(new ClassSnapshot(world.Data.Classes.Classes[k].Id, s.Size[k], total > 0 ? s.Size[k] / total * 100 : 0,
                        s.Income[k], s.Savings[k], s.StandardOfLiving[k], s.Expectations[k], s.Satisfaction[k], s.Radicalism[k], s.Influence[k]));
            }
            var government = world.Government?.Countries.GetValueOrDefault(c.Id) is { } g ? GovernmentSnapshot.Capture(world, c, g) : null;
            var laws = world.Laws?.Countries.GetValueOrDefault(c.Id) is { } l ? LawsSnapshot.Capture(world.Date, l) : null;
            countries[c.Id] = new CountrySnapshot(c.Id, c.System, c.Income, new Dictionary<string, StatValue>(c.Stats, StringComparer.Ordinal),
                ages, classes, government, c.Regime, laws);
        }
        var occupied = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in world.Regions)
            if (r.IsOccupied)
                occupied[r.Id] = r.Controller;
        var currencies = new Dictionary<string, CurrencySnapshot>(StringComparer.Ordinal);
        if (world.Economy is { } economy)
        {
            var banks = world.Data.Currencies.Currencies.ToDictionary(c => c.Id, c => c.Bank, StringComparer.Ordinal);
            foreach (var cur in economy.Currencies.Values)
            {
                var info = new CurrencySnapshot(cur.Id, cur.IsFloating, cur.Anchor, cur.Members.Count, banks.GetValueOrDefault(cur.Id));
                foreach (var id in cur.Members)
                    currencies[id] = info;
            }
        }
        return new WorldSnapshot
        {
            Date = world.Date,
            Speed = speed,
            Paused = paused,
            PlayerCountry = world.PlayerCountry,
            Countries = countries,
            Occupied = occupied,
            Currencies = currencies,
            Journal = world.Journal.Count <= JournalShown ? world.Journal.ToList() : world.Journal.GetRange(world.Journal.Count - JournalShown, JournalShown),
            Version = version,
        };
    }
}

public sealed record CurrencySnapshot(string Code, bool Floating, string? Anchor, int Users, string? Bank);

/// <summary>
/// Класс в снимке: численность с семьями, доля населения (%), доход и сбережения на человека (долл. по ППС в год),
/// уровень жизни, ожидания, удовлетворённость, радикализм (0–100) и влияние (%).
/// </summary>
public sealed record ClassSnapshot(string Id, double Size, double Share, double Income, double Savings,
    double StandardOfLiving, double Expectations, double Satisfaction, double Radicalism, double Influence);

/// <summary>
/// Страна в снимке. Ages — численность по пятилетним группам: сначала 17 групп мужчин, затем 17 женщин.
/// Classes — классы в порядке data/classes.json.
/// </summary>
public sealed record CountrySnapshot(
    string Id,
    PoliticalSystem System,
    IncomeGroup Income,
    IReadOnlyDictionary<string, StatValue> Stats,
    IReadOnlyList<double>? Ages = null,
    IReadOnlyList<ClassSnapshot>? Classes = null,
    GovernmentSnapshot? Government = null,
    Regime Regime = Regime.Democracy,
    LawsSnapshot? Laws = null)
{
    public StatValue? Stat(string id) => Stats.GetValueOrDefault(id);
}
