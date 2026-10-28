using Politeconomy.Sim.Data;

namespace Politeconomy.Sim.Model;

/// <summary>Значение показателя и его происхождение — из него строится подсказка «почему».</summary>
public sealed record StatValue(double Value, int? Year, string Source, string? Method = null, int? N = null)
{
    /// <summary>Значение не из источника, а оценка: заполнение пропуска или расчёт из других показателей.</summary>
    public bool IsEstimate => Source is StatSources.Fill or StatSources.Derived;
}

public static class StatSources
{
    public const string Fill = "fill";
    public const string Derived = "derived";
    public const string Model = "model";
}

public enum PoliticalSystem
{
    PresidentialRepublic,
    ParliamentaryRepublic,
    SemiPresidential,
    ConstitutionalMonarchy,
    AbsoluteMonarchy,
    OnePartyState,
    MilitaryRegime,
}

public enum IncomeGroup
{
    Low,
    LowerMiddle,
    UpperMiddle,
    High,
}

/// <summary>Страна: постоянное описание из данных и изменяемое состояние модели.</summary>
public sealed class Country
{
    public required string Id { get; init; }
    public required CountryRecord Info { get; init; }
    public required PoliticalSystem System { get; set; }
    public required IncomeGroup Income { get; set; }

    /// <summary>Текущие показатели. Значения неизменяемые: модель заменяет запись целиком.</summary>
    public Dictionary<string, StatValue> Stats { get; } = new(StringComparer.Ordinal);

    public string Name => Info.Name;

    public double Stat(string id) => Stats.TryGetValue(id, out var v) ? v.Value : double.NaN;
}

/// <summary>Полное состояние партии. Меняется только в потоке симуляции.</summary>
public sealed class World
{
    public required GameData Data { get; init; }
    public required DateOnly Date { get; set; }
    public required DeterministicRandom Random { get; init; }
    public required ulong Seed { get; init; }
    public string? PlayerCountry { get; set; }
    public required IReadOnlyList<Country> Countries { get; init; }

    readonly Dictionary<string, Country> _byId = new(StringComparer.Ordinal);

    public Country this[string id] => ById(id) ?? throw new KeyNotFoundException($"Нет страны {id}");

    public Country? ById(string id)
    {
        if (_byId.Count == 0)
            foreach (var c in Countries)
                _byId[c.Id] = c;
        return _byId.GetValueOrDefault(id);
    }
}
