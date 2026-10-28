using Politeconomy.Sim.Data;

namespace Politeconomy.Sim.Model;

/// <summary>
/// Значение показателя и его происхождение — из него строится подсказка «почему».
/// Factors — из чего складывается значение, рассчитанное моделью (для подсказки).
/// </summary>
public sealed record StatValue(double Value, int? Year, string Source, string? Method = null, int? N = null,
    IReadOnlyList<StatFactor>? Factors = null)
{
    /// <summary>Значение не из источника, а оценка: заполнение пропуска или расчёт из других показателей.</summary>
    public bool IsEstimate => Source is StatSources.Fill or StatSources.Derived;
}

/// <summary>Слагаемое или причина значения показателя. Format — как в indicators.json (percent, percent_signed…).</summary>
public sealed record StatFactor(string Label, double Value, string Format);

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

/// <summary>
/// Регион страны. Owner — страна, которой регион принадлежит по международному праву;
/// Controller — страна, которая его фактически контролирует. Они расходятся при оккупации и войне.
/// </summary>
public sealed class Region
{
    public required string Id { get; init; }
    public required RegionRecord Info { get; init; }
    public required string Owner { get; init; }
    public required string Controller { get; set; }

    public string Name => Info.Name;
    public bool IsOccupied => Controller != Owner;
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
    public required IReadOnlyList<Region> Regions { get; init; }

    /// <summary>Состояние экономики всех стран; создаётся <see cref="Economy.EconomyModel.Setup"/>.</summary>
    public Economy.WorldEconomy? Economy { get; set; }

    readonly Dictionary<string, Country> _byId = new(StringComparer.Ordinal);
    readonly Dictionary<string, Region> _regionsById = new(StringComparer.Ordinal);

    public Country this[string id] => ById(id) ?? throw new KeyNotFoundException($"Нет страны {id}");

    public Country? ById(string id)
    {
        if (_byId.Count == 0)
            foreach (var c in Countries)
                _byId[c.Id] = c;
        return _byId.GetValueOrDefault(id);
    }

    public Region? RegionById(string id)
    {
        if (_regionsById.Count == 0)
            foreach (var r in Regions)
                _regionsById[r.Id] = r;
        return _regionsById.GetValueOrDefault(id);
    }

    /// <summary>Регионы, которые страна фактически контролирует (свои и занятые).</summary>
    public IEnumerable<Region> RegionsControlledBy(string country) => Regions.Where(r => r.Controller == country);

    /// <summary>Передать контроль над регионом другой стране: захват в войне, мирный договор или возврат.</summary>
    public void TransferControl(string regionId, string controller)
    {
        var region = RegionById(regionId) ?? throw new KeyNotFoundException($"Нет региона {regionId}");
        if (ById(controller) is null)
            throw new KeyNotFoundException($"Нет страны {controller}");
        region.Controller = controller;
    }
}
