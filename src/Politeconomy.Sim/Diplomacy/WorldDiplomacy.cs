using Politeconomy.Sim.Model;
using System.Text.Json.Serialization;

namespace Politeconomy.Sim.Diplomacy;

/// <summary>
/// Дипломатия мира. Memory — память о событиях по парам стран («AAA|BBB», коды по алфавиту), Treaties — договоры,
/// Visits — когда глава последний раз ездил к партнёру. Поправки к модели по странам — в Countries. Торговля пар и
/// отношения не сохраняются: они пересчитываются из данных и состояния. MODEL.md, «Дипломатия».
/// </summary>
public sealed class WorldDiplomacy
{
    public SortedDictionary<string, double> Memory { get; set; } = new(StringComparer.Ordinal);
    public List<Treaty> Treaties { get; set; } = [];
    public SortedDictionary<string, DateOnly> Visits { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, CountryDiplomacy> Countries { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Порядок стран, доли торговли и отношения — пересчитываются (не сохраняются).</summary>
    [JsonIgnore] public Dictionary<string, int> Index { get; set; } = new(StringComparer.Ordinal);
    [JsonIgnore] public string[] Ids { get; set; } = [];
    [JsonIgnore] public double[][] Trade { get; set; } = [];
    [JsonIgnore] public double[][] Relations { get; set; } = [];
    /// <summary>Строй и партия главы каждой страны, по которым посчитаны отношения: сменились — пора пересчитать.</summary>
    [JsonIgnore] public (Regime Regime, string? Party)[] Basis { get; set; } = [];
    /// <summary>Пары соседей по суше и история пар («AAA|BBB») — из данных.</summary>
    [JsonIgnore] public HashSet<string> Neighbors { get; set; } = new(StringComparer.Ordinal);
    [JsonIgnore] public Dictionary<string, double> History { get; set; } = new(StringComparer.Ordinal);

    public CountryDiplomacy this[string country] => Countries[country];

    public static string Key(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
}

/// <summary>Договор двух стран: вид (data/diplomacy.json), с какой даты.</summary>
public sealed record Treaty(string A, string B, string Type, DateOnly Since)
{
    public bool Involves(string country) => A == country || B == country;
    public string Other(string country) => A == country ? B : A;
}

/// <summary>Поправки дипломатии для страны: торговля (множитель), производительность, настроение классов, поправки к модели.</summary>
public sealed class CountryDiplomacy
{
    public double TradeFactor { get; set; } = 1;
    public double Productivity { get; set; } = 1;
    public double[] Mood { get; set; } = [];
    public Dictionary<string, double> Effects { get; set; } = new(StringComparer.Ordinal);
}
