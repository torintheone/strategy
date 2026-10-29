using System.Text.Json.Serialization;

namespace Politeconomy.Sim.Government;

/// <summary>
/// Правительство страны: глава исполнительной власти, министры по постам из data/government.json, кандидаты
/// на посты (появляются, когда игрок решает сменить министра) и парламент.
/// </summary>
public sealed class CountryGovernment
{
    public Character Leader { get; set; } = new();
    /// <summary>Министры по id поста, в порядке постов.</summary>
    public Dictionary<string, Character> Ministers { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<Character>> Candidates { get; set; } = new(StringComparer.Ordinal);
    public Parliament Parliament { get; set; } = new();
    /// <summary>Растёт при каждом изменении состава — по нему интерфейс понимает, что пора перерисовать.</summary>
    public int Version { get; set; }

    public Character? Minister(string post) => Ministers.GetValueOrDefault(post);
}

/// <summary>
/// Парламент до появления партий (этап 4): правящий блок и оппозиция. Kind — real (голосует всерьёз), formal
/// (одобряет почти всё) или none (власть правит указами). Discipline — доля депутатов, голосующих по линии блока.
/// </summary>
public sealed class Parliament
{
    public string Kind { get; set; } = "real";
    public string Name { get; set; } = "";
    public int Seats { get; set; }
    public int RulingSeats { get; set; }
    public double Discipline { get; set; }

    [JsonIgnore]
    public int OppositionSeats => Seats - RulingSeats;

    [JsonIgnore]
    public bool Exists => Kind != "none" && Seats > 0;
}

public sealed class WorldGovernment
{
    /// <summary>Следующий свободный номер персонажа.</summary>
    public int NextId { get; set; } = 1;
    public Dictionary<string, CountryGovernment> Countries { get; set; } = new(StringComparer.Ordinal);

    public CountryGovernment this[string country] => Countries[country];
}
