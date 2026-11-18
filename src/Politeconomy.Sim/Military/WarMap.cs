using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Military;

/// <summary>
/// Что о регионах нужно войне — из данных: соседи по суше и через проливы, доля гор, население и площадь, владелец,
/// кто контролировал регион на старте, регион столицы; население стран по их нынешним регионам (<see cref="Recount"/>).
/// MODEL.md, «Армии и войны».
/// </summary>
public sealed class WarMap
{
    public static readonly WarMap Empty = new();

    public Dictionary<string, IReadOnlyList<string>> Land { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, IReadOnlyList<string>> Sea { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Mountains { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, RegionRecord> Regions { get; } = new(StringComparer.Ordinal);
    /// <summary>Население страны по регионам, которыми она владеет сейчас (по данным регионов).</summary>
    public Dictionary<string, double> CountryPopulation { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Capitals { get; } = new(StringComparer.Ordinal);
    /// <summary>Кто контролировал регион на старте, если не владелец (Крым и Севастополь).</summary>
    public Dictionary<string, string> StartControl { get; } = new(StringComparer.Ordinal);

    public static WarMap Build(GameData data)
    {
        var map = new WarMap();
        foreach (var t in data.Terrain.Regions)
        {
            map.Land[t.Id] = t.Land;
            map.Sea[t.Id] = t.Sea;
            map.Mountains[t.Id] = t.Mountains;
        }
        foreach (var r in data.Regions.Regions)
        {
            map.Regions[r.Id] = r;
            map.CountryPopulation[r.Country] = map.CountryPopulation.GetValueOrDefault(r.Country) + r.Population;
        }
        foreach (var city in data.Cities.Cities.Where(c => c.Capital && map.Regions.ContainsKey(c.Region)))
            map.Capitals.TryAdd(city.Country, city.Region);
        foreach (var c in data.Control.Control)
            map.StartControl[c.Region] = c.Controller;
        return map;
    }

    /// <summary>Население стран по регионам, которыми они владеют сейчас (после мирных договоров).</summary>
    public void Recount(World world)
    {
        CountryPopulation.Clear();
        foreach (var r in world.Regions)
            CountryPopulation[r.Owner] = CountryPopulation.GetValueOrDefault(r.Owner) + r.Info.Population;
    }

    public IEnumerable<(string Id, bool Land)> Neighbors(string region)
    {
        foreach (var n in Land.GetValueOrDefault(region) ?? [])
            yield return (n, true);
        foreach (var n in Sea.GetValueOrDefault(region) ?? [])
            yield return (n, false);
    }

    /// <summary>Доля населения страны-владельца в регионе (по данным регионов).</summary>
    public double Share(string region, string owner) =>
        Regions.TryGetValue(region, out var r) && CountryPopulation.GetValueOrDefault(owner) is var total and > 0 ? r.Population / total : 0;
}
