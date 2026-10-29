using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Geo;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Views;

public sealed record LegendEntry(string Color, string Text);

/// <summary>
/// Раскраска карты для одного режима: цвет каждого региона (hex из data/map_modes.json) и легенда.
/// Регион окрашивается по стране, которая его фактически контролирует. Чистые вычисления без движка.
/// </summary>
public sealed class MapModeView
{
    readonly GameData _data;
    readonly WorldMap _map;
    readonly MapPalettes _palettes;
    readonly Dictionary<string, RegionRecord> _regions;

    public MapModeView(GameData data, WorldMap map, MapModeDef mode)
    {
        _data = data;
        _map = map;
        _palettes = data.MapModes.Palettes;
        _regions = data.Regions.Regions.ToDictionary(r => r.Id, StringComparer.Ordinal);
        Mode = mode;
        Indicator = mode.Indicator is null ? null : data.Indicator(mode.Indicator);
    }

    public MapModeDef Mode { get; }
    public IndicatorDef? Indicator { get; }

    /// <summary>Страна, которая контролирует регион, или null для нейтральной территории.</summary>
    public static string? ControllerOf(MapRegion region, WorldSnapshot snapshot) =>
        region.Country is null ? null : snapshot.ControllerOf(region.Id, region.Country);

    public string ColorOf(MapRegion region, WorldSnapshot snapshot)
    {
        var controller = ControllerOf(region, snapshot);
        if (controller is null || !snapshot.Countries.TryGetValue(controller, out var country))
            return _palettes.Territory;
        if (Mode.Kind == "elections")
            return ElectionWinner(region, snapshot)?.Color ?? _palettes.NoData;
        return Mode.Kind switch
        {
            "political" => PoliticalColor(controller),
            "politics" => _data.MapModes.PoliticsColors.GetValueOrDefault(WorldFactory.SystemId(country.System), _palettes.NoData),
            "income" => _data.MapModes.IncomeGroups.FirstOrDefault(g => g.Id == WorldFactory.IncomeId(country.Income))?.Color ?? _palettes.NoData,
            "parties" => country.Politics?.Head?.Color ?? _palettes.NoData,
            "bins" or "diverging" => ValueColor(country.Stat(Mode.Indicator!)?.Value ?? double.NaN),
            "region_density" => ValueColor(Density(region.Id)),
            _ => _palettes.NoData,
        };
    }

    /// <summary>Партия, победившая в регионе на последних выборах его страны (по принадлежности, а не по контролю).</summary>
    public static PartySnapshot? ElectionWinner(MapRegion region, WorldSnapshot snapshot) =>
        region.Country is { } owner && snapshot.Countries.TryGetValue(owner, out var country)
        && country.Politics?.LastElection?.Regions.TryGetValue(region.Id, out var party) == true
            ? country.Politics.Party(party)
            : null;

    public string PoliticalColor(string country)
    {
        var index = _map.Country(country)?.ColorIndex ?? 0;
        return _palettes.Political[Math.Max(index, 0) % _palettes.Political.Count];
    }

    /// <summary>Плотность населения региона, чел./км².</summary>
    public double Density(string regionId) =>
        _regions.TryGetValue(regionId, out var r) && r.AreaKm2 > 0 ? r.Population / r.AreaKm2 : double.NaN;

    public string ValueColor(double value)
    {
        if (double.IsNaN(value) || Mode.Thresholds is null)
            return _palettes.NoData;
        return ClassColor(ClassOf(value, Mode.Thresholds));
    }

    string ClassColor(int k) =>
        Mode.Kind == "diverging" ? DivergingColor(k, Mode.Thresholds!.Count) : SequentialColor(k, Mode.Thresholds!.Count);

    /// <summary>Номер класса: 0 — меньше первого порога, N — не меньше последнего.</summary>
    public static int ClassOf(double value, IReadOnlyList<double> thresholds)
    {
        var k = 0;
        while (k < thresholds.Count && value >= thresholds[k])
            k++;
        return k;
    }

    string SequentialColor(int k, int thresholdCount)
    {
        var ramp = _palettes.Sequential;
        var index = thresholdCount == 0 ? 0 : (int)Math.Round(k * (ramp.Count - 1) / (double)thresholdCount);
        return ramp[Math.Clamp(index, 0, ramp.Count - 1)];
    }

    string DivergingColor(int k, int thresholdCount)
    {
        var arm = thresholdCount / 2;
        if (k < arm)
            return _palettes.DivergingNegative[Math.Min(k, _palettes.DivergingNegative.Count - 1)];
        if (k == arm)
            return _palettes.DivergingNeutral;
        return _palettes.DivergingPositive[Math.Min(k - arm - 1, _palettes.DivergingPositive.Count - 1)];
    }

    public IReadOnlyList<LegendEntry> Legend()
    {
        switch (Mode.Kind)
        {
            case "politics":
                return _data.MapModes.PoliticsColors
                    .Select(kv => new LegendEntry(kv.Value, _data.Politics.Systems.GetValueOrDefault(kv.Key, kv.Key)))
                    .ToList();
            case "income":
                return _data.MapModes.IncomeGroups.Select(g => new LegendEntry(g.Color, g.Name)).ToList();
            case "parties" or "elections":
                return _data.Parties.Families.Append(_data.Parties.Power)
                    .Select(f => new LegendEntry(f.Color, f.Name))
                    .Append(new LegendEntry(_palettes.NoData, Mode.Kind == "parties" ? "партий нет" : "выборов нет"))
                    .ToList();
            case "bins" or "diverging" or "region_density" when Mode.Thresholds is { Count: > 0 } t:
            {
                var list = new List<LegendEntry>();
                var format = Indicator?.Format ?? "number1";
                var unit = Mode.Unit is null ? "" : Fmt.Nbsp + Mode.Unit;
                string F(double v) => Fmt.Compact(format, v);
                for (var k = 0; k <= t.Count; k++)
                {
                    var text = k == 0 ? $"меньше {F(t[0])}{unit}"
                        : k == t.Count ? $"{F(t[^1])}{unit} и больше"
                        : $"{F(t[k - 1])} — {F(t[k])}{unit}";
                    list.Add(new LegendEntry(ClassColor(k), text));
                }
                if (Mode.Kind != "region_density")
                    list.Add(new LegendEntry(_palettes.NoData, "нет данных"));
                return list;
            }
            default:
                return [];
        }
    }

    /// <summary>Строка для всплывающей подсказки над регионом в этом режиме.</summary>
    public string ValueText(MapRegion region, WorldSnapshot snapshot)
    {
        var controller = ControllerOf(region, snapshot);
        if (controller is null || !snapshot.Countries.TryGetValue(controller, out var country))
            return "";
        if (Mode.Kind == "elections")
            return ElectionWinner(region, snapshot) is { } winner && region.Country is { } owner
                ? $"Выборы {snapshot.Countries[owner].Politics!.LastElection!.Date.Year}: в регионе победила {Fmt.Quote(winner.Name)}"
                : "Выборов нет";
        return Mode.Kind switch
        {
            "politics" => _data.Politics.Systems.GetValueOrDefault(WorldFactory.SystemId(country.System), ""),
            "income" => _data.MapModes.IncomeGroups.FirstOrDefault(g => g.Id == WorldFactory.IncomeId(country.Income))?.Name ?? "",
            "parties" => country.Politics?.Head is { } head
                ? $"Правит: {head.Name} ({_data.Family(head.Family).Name.ToLowerInvariant()}), в опросах {Fmt.Value("percent", head.Poll * 100)}"
                : "Партий нет",
            "bins" or "diverging" when Indicator is not null => ValueLine(country.Stat(Indicator.Id)),
            "region_density" => $"Плотность: {Fmt.Number(Density(region.Id), 0)}{Fmt.Nbsp}{Mode.Unit}",
            _ => "",
        };
    }

    string ValueLine(StatValue? stat)
    {
        if (stat is null)
            return $"{Indicator!.Name}: нет данных";
        return $"{Indicator!.Name}: {Fmt.Value(Indicator.Format, stat.Value, Indicator.Unit)}";
    }
}
