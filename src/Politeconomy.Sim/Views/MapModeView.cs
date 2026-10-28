using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Geo;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Views;

public sealed record LegendEntry(string Color, string Text);

/// <summary>
/// Раскраска карты для одного режима: цвет каждой страны (hex из data/map_modes.json) и легенда.
/// Чистые вычисления без движка — интерфейс только переводит hex в цвет.
/// </summary>
public sealed class MapModeView
{
    readonly GameData _data;
    readonly MapPalettes _palettes;

    public MapModeView(GameData data, MapModeDef mode)
    {
        _data = data;
        _palettes = data.MapModes.Palettes;
        Mode = mode;
        Indicator = mode.Indicator is null ? null : data.Indicator(mode.Indicator);
    }

    public MapModeDef Mode { get; }
    public IndicatorDef? Indicator { get; }

    public string ColorOf(MapRegion region, CountrySnapshot? country)
    {
        if (!region.IsCountry || country is null)
            return _palettes.Territory;
        return Mode.Kind switch
        {
            "political" => _palettes.Political[Math.Max(region.ColorIndex, 0) % _palettes.Political.Count],
            "politics" => _data.MapModes.PoliticsColors.GetValueOrDefault(WorldFactory.SystemId(country.System), _palettes.NoData),
            "income" => _data.MapModes.IncomeGroups.FirstOrDefault(g => g.Id == WorldFactory.IncomeId(country.Income))?.Color ?? _palettes.NoData,
            "bins" or "diverging" => ValueColor(country.Stat(Mode.Indicator!)?.Value ?? double.NaN),
            _ => _palettes.NoData,
        };
    }

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
            case "bins" or "diverging" when Mode.Thresholds is { Count: > 0 } t && Indicator is not null:
            {
                var list = new List<LegendEntry>();
                string F(double v) => Fmt.Compact(Indicator.Format, v);
                for (var k = 0; k <= t.Count; k++)
                {
                    var text = k == 0 ? $"меньше {F(t[0])}"
                        : k == t.Count ? $"{F(t[^1])} и больше"
                        : $"{F(t[k - 1])} — {F(t[k])}";
                    list.Add(new LegendEntry(ClassColor(k), text));
                }
                list.Add(new LegendEntry(_palettes.NoData, "нет данных"));
                return list;
            }
            default:
                return [];
        }
    }

    /// <summary>Строка для всплывающей подсказки над страной в этом режиме.</summary>
    public string ValueText(CountrySnapshot country) => Mode.Kind switch
    {
        "politics" => _data.Politics.Systems.GetValueOrDefault(WorldFactory.SystemId(country.System), ""),
        "income" => _data.MapModes.IncomeGroups.FirstOrDefault(g => g.Id == WorldFactory.IncomeId(country.Income))?.Name ?? "",
        "bins" or "diverging" when Indicator is not null => ValueLine(country.Stat(Indicator.Id)),
        _ => "",
    };

    string ValueLine(StatValue? stat)
    {
        if (stat is null)
            return $"{Indicator!.Name}: нет данных";
        var prefix = stat.IsEstimate ? "≈" : "";
        return $"{Indicator!.Name}: {prefix}{Fmt.Value(Indicator.Format, stat.Value, Indicator.Unit)}";
    }
}
