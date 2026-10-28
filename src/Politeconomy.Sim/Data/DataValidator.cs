using System.Text.RegularExpressions;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Data;

public sealed record DataProblem(string File, string Message)
{
    public override string ToString() => $"{File}: {Message}";
}

/// <summary>Проверка ссылок и диапазонов во всех файлах data/. Запускается в CI командой <c>Politeconomy.Tools validate</c>.</summary>
public static partial class DataValidator
{
    /// <summary>Показатели, без которых страна не может начать партию.</summary>
    public static readonly string[] RequiredStats =
    [
        "population", "gdp_usd", "gdp_per_capita_usd", "gdp_growth_pct", "inflation_pct",
        "unemployment_pct", "gov_debt_pct_gdp", "life_expectancy", "labor_force",
    ];

    /// <summary>Допустимые диапазоны значений: всё, что вне их, — почти наверняка ошибка данных.</summary>
    public static readonly IReadOnlyDictionary<string, (double Min, double Max)> Ranges = new Dictionary<string, (double, double)>
    {
        ["population"] = (1e3, 2e9),
        ["gdp_usd"] = (1e6, 1e14),
        ["gdp_per_capita_usd"] = (50, 300_000),
        ["gdp_ppp_per_capita"] = (300, 300_000),
        ["gdp_growth_pct"] = (-60, 60),
        ["inflation_pct"] = (-30, 2000),
        ["unemployment_pct"] = (0, 60),
        ["gov_debt_pct_gdp"] = (0, 400),
        ["tax_revenue_pct_gdp"] = (0, 80),
        ["exports_pct_gdp"] = (0, 300),
        ["imports_pct_gdp"] = (0, 300),
        ["agriculture_pct_gdp"] = (0, 100),
        ["industry_pct_gdp"] = (0, 100),
        ["life_expectancy"] = (30, 95),
        ["fertility_rate"] = (0.5, 9),
        ["urban_pct"] = (0, 100),
        ["age_0_14_pct"] = (0, 60),
        ["age_65_plus_pct"] = (0, 50),
        ["labor_force"] = (1e2, 1.5e9),
        ["military_pct_gdp"] = (0, 40),
        ["electricity_access_pct"] = (0, 100),
        ["gini"] = (15, 75),
    };

    static readonly HashSet<string> Formats = ["people", "money_big", "money", "percent", "percent_signed", "percent_gdp", "years", "number1", "number2"];
    static readonly HashSet<string> MapKinds = ["political", "bins", "diverging", "politics", "income"];

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    public static IReadOnlyList<DataProblem> Validate(GameData data)
    {
        var problems = new List<DataProblem>();
        void Add(string file, string message) => problems.Add(new DataProblem(file, message));

        // --- indicators.json
        var indicators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var i in data.Indicators.Indicators)
        {
            if (!indicators.Add(i.Id))
                Add("indicators.json", $"показатель {i.Id} описан дважды");
            if (!Formats.Contains(i.Format))
                Add("indicators.json", $"у показателя {i.Id} неизвестный формат «{i.Format}»");
        }

        // --- countries.json
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var countries = data.Countries.Countries;
        if (countries.Count < 190)
            Add("countries.json", $"стран {countries.Count}, ожидается около 193");
        foreach (var c in countries)
        {
            if (!ids.Add(c.Id))
                Add("countries.json", $"страна {c.Id} встречается дважды");
            if (string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.ShortName))
                Add("countries.json", $"у страны {c.Id} нет названия");
            try { WorldFactory.ParseIncome(c.IncomeGroup); }
            catch (DataException e) { Add("countries.json", $"{c.Id}: {e.Message}"); }
            foreach (var key in RequiredStats)
                if (!c.Stats.ContainsKey(key))
                    Add("countries.json", $"у страны {c.Id} нет показателя {key}");
            foreach (var (key, s) in c.Stats)
            {
                if (!indicators.Contains(key))
                    Add("countries.json", $"{c.Id}: показатель {key} не описан в indicators.json");
                if (double.IsNaN(s.Value) || double.IsInfinity(s.Value))
                    Add("countries.json", $"{c.Id}.{key}: значение не число");
                else if (Ranges.TryGetValue(key, out var r) && (s.Value < r.Min || s.Value > r.Max))
                    Add("countries.json", $"{c.Id}.{key} = {s.Value} вне диапазона {r.Min}…{r.Max}");
                var knownSource = s.Source.StartsWith("wb:", StringComparison.Ordinal)
                    || s.Source.StartsWith("imf:", StringComparison.Ordinal)
                    || s.Source is StatSources.Fill or StatSources.Derived;
                if (!knownSource)
                    Add("countries.json", $"{c.Id}.{key}: неизвестный источник «{s.Source}»");
                if (s.Source == StatSources.Fill && string.IsNullOrEmpty(s.Method))
                    Add("countries.json", $"{c.Id}.{key}: заполнение пропуска без описания способа");
            }
        }

        // --- politics.json
        var seenPolitics = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in data.Politics.Countries)
        {
            if (!ids.Contains(p.Id))
                Add("politics.json", $"страна {p.Id} не найдена в countries.json");
            if (!seenPolitics.Add(p.Id))
                Add("politics.json", $"страна {p.Id} встречается дважды");
            if (!data.Politics.Systems.ContainsKey(p.System))
                Add("politics.json", $"{p.Id}: тип системы «{p.System}» не описан в разделе systems");
            try { WorldFactory.ParseSystem(p.System); }
            catch (DataException e) { Add("politics.json", $"{p.Id}: {e.Message}"); }
        }
        foreach (var id in ids.Where(id => !seenPolitics.Contains(id)))
            Add("politics.json", $"нет политической системы для страны {id}");

        // --- map/world.json
        var mapIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in data.Map.Countries)
        {
            if (!ids.Contains(c.Id))
                Add("map/world.json", $"на карте страна {c.Id}, которой нет в countries.json");
            mapIds.Add(c.Id);
            if (c.Label.Count != 2)
                Add("map/world.json", $"у страны {c.Id} нет точки для подписи");
            if (c.Color < 0 || c.Color >= data.Map.PoliticalColors)
                Add("map/world.json", $"у страны {c.Id} цвет {c.Color} вне палитры");
            foreach (var n in c.Neighbors.Where(n => !ids.Contains(n)))
                Add("map/world.json", $"у страны {c.Id} неизвестный сосед {n}");
            CheckPolygons("map/world.json", c.Id, c.Polygons, Add);
        }
        foreach (var t in data.Map.Territories)
            CheckPolygons("map/world.json", t.Id, t.Polygons, Add);
        foreach (var id in ids.Where(id => !mapIds.Contains(id)))
            Add("map/world.json", $"страны {id} нет на карте");

        // --- map/cities.json
        var capitals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var city in data.Cities.Cities)
        {
            if (!ids.Contains(city.Country))
                Add("map/cities.json", $"город {city.Name}: неизвестная страна {city.Country}");
            if (city.Capital)
                capitals[city.Country] = capitals.GetValueOrDefault(city.Country) + 1;
        }
        foreach (var (country, count) in capitals.Where(kv => kv.Value > 1))
            Add("map/cities.json", $"у страны {country} {count} столицы");

        // --- map_modes.json
        var modes = data.MapModes;
        var pal = modes.Palettes;
        foreach (var color in pal.Political.Concat(pal.Sequential).Concat(pal.DivergingNegative).Concat(pal.DivergingPositive)
                     .Append(pal.DivergingNeutral).Append(pal.NoData).Append(pal.Territory)
                     .Concat(modes.PoliticsColors.Values).Concat(modes.IncomeGroups.Select(g => g.Color)))
            if (!HexColor().IsMatch(color))
                Add("map_modes.json", $"цвет «{color}» не в формате #rrggbb");
        if (pal.Political.Count < data.Map.PoliticalColors)
            Add("map_modes.json", $"в палитре political {pal.Political.Count} цветов, карте нужно {data.Map.PoliticalColors}");
        var modeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in modes.Modes)
        {
            if (!modeIds.Add(m.Id))
                Add("map_modes.json", $"режим {m.Id} описан дважды");
            if (!MapKinds.Contains(m.Kind))
                Add("map_modes.json", $"режим {m.Id}: неизвестный вид «{m.Kind}»");
            if (m.Kind is not ("bins" or "diverging"))
                continue;
            if (m.Indicator is null || !indicators.Contains(m.Indicator))
                Add("map_modes.json", $"режим {m.Id}: показатель «{m.Indicator}» не описан в indicators.json");
            var t = m.Thresholds ?? [];
            if (t.Count == 0)
                Add("map_modes.json", $"режим {m.Id}: нет порогов");
            for (var i = 1; i < t.Count; i++)
                if (t[i] <= t[i - 1])
                    Add("map_modes.json", $"режим {m.Id}: пороги должны расти");
            if (m.Kind == "bins" && t.Count + 1 > pal.Sequential.Count)
                Add("map_modes.json", $"режим {m.Id}: классов {t.Count + 1}, а цветов в sequential {pal.Sequential.Count}");
            if (m.Kind == "diverging" && (t.Count % 2 != 0 || t.Count / 2 != pal.DivergingNegative.Count || t.Count / 2 != pal.DivergingPositive.Count))
                Add("map_modes.json", $"режим {m.Id}: число порогов не совпадает с плечами расходящейся палитры");
        }
        foreach (var system in data.Politics.Systems.Keys.Where(k => !modes.PoliticsColors.ContainsKey(k)))
            Add("map_modes.json", $"нет цвета для политической системы {system}");

        // --- scenario.json и balance.json
        if (data.Scenario.PlayerCountry is { } player && !ids.Contains(player))
            Add("scenario.json", $"страна игрока {player} не найдена");
        if (data.Scenario.WarmupMonths is < 0 or > 120)
            Add("scenario.json", "разогрев должен быть от 0 до 120 месяцев");
        var speeds = data.Balance.Time.DaysPerSecond;
        if (speeds.Count != 5)
            Add("balance.json", "нужно ровно 5 скоростей времени");
        for (var i = 0; i < speeds.Count; i++)
            if (speeds[i] <= 0 || (i > 0 && speeds[i] <= speeds[i - 1]))
                Add("balance.json", "скорости времени должны быть положительными и расти");

        return problems;
    }

    static void CheckPolygons(string file, string id, IReadOnlyList<MapPolygonRecord> polygons, Action<string, string> add)
    {
        if (polygons.Count == 0)
            add(file, $"у {id} нет многоугольников");
        foreach (var p in polygons)
        {
            var vertices = p.Rings.Sum(r => r.Count) / 2;
            if (p.Rings.Any(r => r.Count < 6 || r.Count % 2 != 0))
                add(file, $"у {id} кольцо короче трёх точек или с нечётным числом координат");
            if (p.Triangles.Count == 0 || p.Triangles.Count % 3 != 0)
                add(file, $"у {id} число индексов треугольников не кратно трём");
            if (p.Triangles.Any(i => i < 0 || i >= vertices))
                add(file, $"у {id} индекс треугольника за пределами вершин");
        }
    }
}
