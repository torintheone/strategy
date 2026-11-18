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
/// Парламент. Kind — real (голосует всерьёз), formal (одобряет почти всё) или none (власть правит указами).
/// Discipline — доля депутатов, голосующих по линии своей фракции. Фракции — партии с местами; где партий нет,
/// две фракции без партии: правящий блок и оппозиция.
/// </summary>
public sealed class Parliament
{
    public string Kind { get; set; } = "real";
    public string Name { get; set; } = "";
    public int Seats { get; set; }
    public double Discipline { get; set; }
    public List<Faction> Factions { get; set; } = [];

    [JsonIgnore]
    public int RulingSeats => Factions.Where(f => f.Ruling).Sum(f => f.Seats);

    [JsonIgnore]
    public int OppositionSeats => Seats - RulingSeats;

    [JsonIgnore]
    public bool Exists => Kind != "none" && Seats > 0;

    /// <summary>Парламент из двух блоков без партий: правящего и оппозиции.</summary>
    public static Parliament Blocks(string kind, string name, int seats, int ruling, double discipline) => new()
    {
        Kind = kind,
        Name = name,
        Seats = seats,
        Discipline = discipline,
        Factions = seats <= 0 ? [] :
        [
            new Faction { Seats = ruling, Ruling = true },
            new Faction { Seats = seats - ruling, Ruling = false },
        ],
    };
}

/// <summary>Фракция: партия (null — правящий блок или оппозиция без партий), её места и входит ли она в правящую коалицию.</summary>
public sealed class Faction
{
    public string? Party { get; set; }
    public int Seats { get; set; }
    public bool Ruling { get; set; }
}

public sealed class WorldGovernment
{
    /// <summary>Следующий свободный номер персонажа.</summary>
    public int NextId { get; set; } = 1;
    public Dictionary<string, CountryGovernment> Countries { get; set; } = new(StringComparer.Ordinal);

    public CountryGovernment this[string country] => Countries[country];
}
