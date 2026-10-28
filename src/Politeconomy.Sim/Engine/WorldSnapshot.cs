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
    public required long Version { get; init; }

    public static WorldSnapshot Capture(World world, int speed, bool paused, long version)
    {
        var countries = new Dictionary<string, CountrySnapshot>(world.Countries.Count, StringComparer.Ordinal);
        foreach (var c in world.Countries)
            countries[c.Id] = new CountrySnapshot(c.Id, c.System, c.Income, new Dictionary<string, StatValue>(c.Stats, StringComparer.Ordinal));
        return new WorldSnapshot
        {
            Date = world.Date,
            Speed = speed,
            Paused = paused,
            PlayerCountry = world.PlayerCountry,
            Countries = countries,
            Version = version,
        };
    }
}

public sealed record CountrySnapshot(
    string Id,
    PoliticalSystem System,
    IncomeGroup Income,
    IReadOnlyDictionary<string, StatValue> Stats)
{
    public StatValue? Stat(string id) => Stats.GetValueOrDefault(id);
}
