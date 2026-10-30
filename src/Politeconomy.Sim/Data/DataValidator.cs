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
        ["gdp_per_capita_usd"] = (50, 1_000_000),
        ["gdp_ppp_per_capita"] = (300, 1_000_000),
        ["gdp_growth_pct"] = (-60, 60),
        ["inflation_pct"] = (-30, 2000),
        ["unemployment_pct"] = (0, 80),
        ["gov_debt_pct_gdp"] = (0, 400),
        ["budget_balance_pct_gdp"] = (-60, 60),
        ["gov_revenue_pct_gdp"] = (0, 150),
        ["gov_spending_pct_gdp"] = (0, 150),
        ["current_account_pct_gdp"] = (-100, 100),
        ["services_pct_gdp"] = (0, 100),
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
        ["manufacturing_pct_gdp"] = (0, 100),
        ["investment_pct_gdp"] = (0, 80),
        ["interest_pct_gdp"] = (0, 30),
        ["policy_rate_pct"] = (-1, 100),
        ["bond_yield_pct"] = (-1, 100),
        ["credit_rating"] = (0, 21),
        ["defaults"] = (0, 20),
        ["exchange_rate_change_pct"] = (-90, 1000),
        ["self_employed_pct"] = (0, 100),
        ["tertiary_enrollment_pct"] = (0, 200),
        ["standard_of_living"] = (0, 100),
        ["satisfaction"] = (0, 100),
        ["radicalism"] = (0, 100),
        ["democracy_index"] = (0, 100),
        ["rule_of_law_index"] = (0, 100),
        ["judicial_independence_index"] = (0, 100),
        ["press_freedom_index"] = (0, 100),
        ["civil_liberties_index"] = (0, 100),
        ["corruption_index"] = (0, 100),
        ["government_effectiveness"] = (0, 100),
        ["legitimacy"] = (0, 100),
        ["unrest_index"] = (0, 100),
        ["education_quality"] = (0, 100),
        ["literacy_pct"] = (0, 100),
        ["human_capital"] = (30, 300),
        ["doctors_per_1000"] = (0, 15),
        ["hospital_beds_per_1000"] = (0, 20),
        ["health_access_pct"] = (0, 100),
        ["disease_burden_pct"] = (0, 30),
        ["infant_mortality"] = (0, 200),
        ["housing_m2_per_person"] = (2, 120),
        ["housing_price_index"] = (10, 1000),
        ["housing_shortage_pct"] = (0, 100),
        ["crime_index"] = (0, 100),
        ["pollution_index"] = (0, 100),
        ["electricity_kwh_per_capita"] = (0, 60000),
        ["power_shortage_pct"] = (0, 100),
        ["power_reserve_pct"] = (-100, 1000),
        ["power_imports_pct"] = (-400, 100),
        ["electricity_price_index"] = (5, 2000),
        ["low_carbon_power_pct"] = (0, 100),
        ["energy_import_dependence_pct"] = (0, 100),
        ["infrastructure_index"] = (0, 200),
        ["infrastructure_condition_pct"] = (0, 100),
        ["approval_pct"] = (0, 100),
        ["ruling_poll_pct"] = (0, 100),
    };

    static readonly HashSet<string> Formats = ["people", "money_big", "money", "percent", "percent_signed", "percent_gdp", "percent_gdp_signed", "years", "number0", "number1", "number2", "rating"];
    static readonly HashSet<string> MapKinds = ["political", "bins", "diverging", "politics", "income", "region_density", "parties", "elections"];

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
                    || s.Source.StartsWith("bis:", StringComparison.Ordinal)
                    || s.Source.StartsWith("owid:", StringComparison.Ordinal)
                    || s.Source is StatSources.Fill or StatSources.Derived or "natural_earth";
                if (!knownSource)
                    Add("countries.json", $"{c.Id}.{key}: неизвестный источник «{s.Source}»");
                if (s.Source == StatSources.Fill && string.IsNullOrEmpty(s.Method))
                    Add("countries.json", $"{c.Id}.{key}: заполнение пропуска без описания способа");
            }
        }

        // --- regions.json и control.json
        var regionIds = new HashSet<string>(StringComparer.Ordinal);
        var regionCountry = new Dictionary<string, string>(StringComparer.Ordinal);
        var regionPop = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var r in data.Regions.Regions)
        {
            if (!regionIds.Add(r.Id))
                Add("regions.json", $"регион {r.Id} встречается дважды");
            regionCountry[r.Id] = r.Country;
            if (!ids.Contains(r.Country))
                Add("regions.json", $"регион {r.Id} ({r.Name}): неизвестная страна {r.Country}");
            if (string.IsNullOrWhiteSpace(r.Name))
                Add("regions.json", $"у региона {r.Id} нет названия");
            if (r.Population <= 0 || r.AreaKm2 <= 0)
                Add("regions.json", $"у региона {r.Id} ({r.Name}) нет населения или площади");
            regionPop[r.Country] = regionPop.GetValueOrDefault(r.Country) + r.Population;
        }
        foreach (var c in countries)
        {
            if (!regionPop.ContainsKey(c.Id))
                Add("regions.json", $"у страны {c.Id} нет ни одного региона");
            else if (c.Stats.TryGetValue("population", out var pop) && regionPop[c.Id] < pop.Value * 0.95)
                Add("regions.json", $"население регионов {c.Id} меньше населения страны: {regionPop[c.Id]:F0} < {pop.Value:F0}");
        }
        foreach (var c in data.Control.Control)
        {
            if (!regionIds.Contains(c.Region))
                Add("control.json", $"неизвестный регион {c.Region}");
            if (!ids.Contains(c.Controller))
                Add("control.json", $"регион {c.Region}: неизвестная страна {c.Controller}");
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
            try { WorldFactory.ParseRegime(p.Regime); }
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
            if (c.Outline.Count == 0)
                Add("map/world.json", $"у страны {c.Id} нет контура");
        }
        foreach (var id in ids.Where(id => !mapIds.Contains(id)))
            Add("map/world.json", $"страны {id} нет на карте");
        var mapRegions = new HashSet<string>(StringComparer.Ordinal);
        var territories = data.Map.Territories.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var r in data.Map.Regions)
        {
            mapRegions.Add(r.Id);
            if (r.Country is null)
            {
                if (r.Territory is null || !territories.Contains(r.Territory))
                    Add("map/world.json", $"регион {r.Id} без страны и без нейтральной территории");
            }
            else if (!regionIds.Contains(r.Id) || regionCountry[r.Id] != r.Country)
                Add("map/world.json", $"регион {r.Id} на карте не совпадает с regions.json");
            CheckPolygons("map/world.json", r.Id, r.Polygons, Add);
        }
        foreach (var id in regionIds.Where(id => !mapRegions.Contains(id)))
            Add("map/world.json", $"региона {id} нет на карте");

        // --- map/cities.json
        var capitals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var city in data.Cities.Cities)
        {
            if (!ids.Contains(city.Country))
                Add("map/cities.json", $"город {city.Name}: неизвестная страна {city.Country}");
            if (!regionCountry.TryGetValue(city.Region, out var owner))
                Add("map/cities.json", $"город {city.Name}: неизвестный регион {city.Region}");
            else if (owner != city.Country)
                Add("map/cities.json", $"город {city.Name}: регион {city.Region} принадлежит {owner}, а не {city.Country}");
            if (city.Capital)
                capitals[city.Country] = capitals.GetValueOrDefault(city.Country) + 1;
        }
        foreach (var (country, count) in capitals.Where(kv => kv.Value > 1))
            Add("map/cities.json", $"у страны {country} {count} столицы");
        foreach (var id in ids.Where(id => !capitals.ContainsKey(id)))
            Add("map/cities.json", $"у страны {id} нет столицы");

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
            if (m.Kind is not ("bins" or "diverging" or "region_density"))
                continue;
            if (m.Kind != "region_density" && (m.Indicator is null || !indicators.Contains(m.Indicator)))
                Add("map_modes.json", $"режим {m.Id}: показатель «{m.Indicator}» не описан в indicators.json");
            var t = m.Thresholds ?? [];
            if (t.Count == 0)
                Add("map_modes.json", $"режим {m.Id}: нет порогов");
            for (var i = 1; i < t.Count; i++)
                if (t[i] <= t[i - 1])
                    Add("map_modes.json", $"режим {m.Id}: пороги должны расти");
            if (m.Kind is "bins" or "region_density" && t.Count + 1 > pal.Sequential.Count)
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

        ValidateEconomy(data, Add);
        ValidateGovernment(data, Add);
        ValidateLaws(data, Add);
        ValidatePolitics(data, Add);
        ValidateGroups(data, Add);
        ValidatePrograms(data, Add);
        ValidateEnergy(data, Add);
        ValidateInfrastructure(data, Add);
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
