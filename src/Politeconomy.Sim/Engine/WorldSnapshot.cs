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
    /// <summary>Валюта каждой страны: код, режим курса, якорь и сколько стран ею пользуются.</summary>
    public required IReadOnlyDictionary<string, CurrencySnapshot> Currencies { get; init; }
    public required long Version { get; init; }

    /// <summary>Кто контролирует регион: оккупант или, если его нет, владелец.</summary>
    public string ControllerOf(string regionId, string owner) => Occupied.GetValueOrDefault(regionId, owner);

    public static WorldSnapshot Capture(World world, int speed, bool paused, long version)
    {
        var countries = new Dictionary<string, CountrySnapshot>(world.Countries.Count, StringComparer.Ordinal);
        foreach (var c in world.Countries)
            countries[c.Id] = new CountrySnapshot(c.Id, c.System, c.Income, new Dictionary<string, StatValue>(c.Stats, StringComparer.Ordinal));
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
            Version = version,
        };
    }
}

public sealed record CurrencySnapshot(string Code, bool Floating, string? Anchor, int Users, string? Bank);

public sealed record CountrySnapshot(
    string Id,
    PoliticalSystem System,
    IncomeGroup Income,
    IReadOnlyDictionary<string, StatValue> Stats)
{
    public StatValue? Stat(string id) => Stats.GetValueOrDefault(id);
}
