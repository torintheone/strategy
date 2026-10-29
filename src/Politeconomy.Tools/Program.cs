using System.Diagnostics;
using Politeconomy.Sim;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Economy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

// Консольные инструменты Politeconomy: проверка данных и прогоны мира без графики.
//   validate            — проверить data/*.json (ссылки, диапазоны, палитры, коэффициенты)
//   run [--years N]     — прогнать мир N лет без графики и показать время шага
//   check [--years N]   — прогон и проверки правдоподобия (по умолчанию 50 лет):
//                         разогрев не уводит показатели от данных, мировой рост 1–5% в год, нет NaN и выходов за диапазоны
//   report [--years N] [--country ISO] — таблица показателей стран по годам
//   classes [--years N] [--country ISO] — классы стран: численность, доходы, уровень жизни, настроение, влияние
//   government [--country ISO] [--years N] — правительство и парламент страны, журнал отставок
// Общий ключ: --data <папка> — где лежит data/ (по умолчанию ищется рядом и выше).

var command = args.FirstOrDefault() ?? "help";
var dataDir = Option("--data") ?? DataDirectory.Find(Environment.CurrentDirectory, AppContext.BaseDirectory);
var years = int.TryParse(Option("--years"), out var y) ? y : 50;

if (command is "help" or "--help" or "-h")
{
    Console.WriteLine("Politeconomy.Tools validate | run [--years N] | check [--years N] | report [--years N] [--country ISO] | classes [--years N] [--country ISO] [--data <папка>]");
    return 0;
}
if (dataDir is null)
{
    Console.Error.WriteLine("Не найдена папка data/ с countries.json. Укажите её ключом --data.");
    return 2;
}

GameData data;
try
{
    data = GameData.Load(dataDir);
}
catch (DataException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

switch (command)
{
    case "validate":
    {
        var problems = DataValidator.Validate(data);
        foreach (var p in problems)
            Console.Error.WriteLine("ОШИБКА " + p);
        var estimates = data.Countries.Countries.Sum(c => c.Stats.Values.Count(s => s.Source is "fill" or "derived"));
        Console.WriteLine($"Стран: {data.Countries.Countries.Count}, оценок вместо данных: {estimates}, городов: {data.Cities.Cities.Count}");
        Console.WriteLine(problems.Count == 0 ? "Данные в порядке." : $"Найдено ошибок: {problems.Count}");
        return problems.Count == 0 ? 0 : 1;
    }
    case "run":
    case "check":
    case "report":
        return Run(command);
    case "trace":
        return Trace();
    case "classes":
        return Classes();
    case "government":
        return Government();
    default:
        Console.Error.WriteLine($"Неизвестная команда «{command}». Запустите с help.");
        return 2;
}

int Run(string mode)
{
    var timer = Stopwatch.StartNew();
    var start = GameSetup.NewGame(data);
    var sim = start.Simulation;
    var world = sim.World;
    Console.WriteLine($"Новая партия с разогревом {data.Scenario.WarmupMonths} мес. за {timer.Elapsed.TotalSeconds:F1} с.");
    var failures = 0;
    if (start.Warmup is { } warmup)
        failures += WarmupCheck(warmup, mode == "check");

    var focus = Option("--country");
    var history = new List<(int Year, double WorldGdp, Dictionary<string, Dictionary<string, double>> Stats)>();
    void Record()
    {
        var snapshot = world.Countries.ToDictionary(c => c.Id, c => c.Stats.ToDictionary(kv => kv.Key, kv => kv.Value.Value));
        history.Add((world.Date.Year, WorldRealGdp(world), snapshot));
    }
    Record();

    var end = world.Date.AddYears(years);
    timer.Restart();
    var days = 0;
    var worstDayMs = 0.0;
    while (world.Date < end)
    {
        var t = timer.Elapsed.TotalMilliseconds;
        sim.StepDay();
        worstDayMs = Math.Max(worstDayMs, timer.Elapsed.TotalMilliseconds - t);
        days++;
        if (world.Date is { Month: 1, Day: 1 })
            Record();
    }
    Console.WriteLine($"Прогон: {years} лет ({days} дней) до {Fmt.Date(world.Date)} за {timer.Elapsed.TotalSeconds:F1} с; самый долгий день {worstDayMs:F2} мс.");
    if (mode == "run")
        return 0;

    PrintWorld(history);
    foreach (var id in (focus is null ? ["USA", "CHN", "DEU", "RUS", "IND", "BRA", "SAU", "TUR", "JPN", "NGA"] : new[] { focus }).Where(id => world.ById(id) is not null))
        PrintCountry(history, id);
    if (mode == "report")
        return 0;

    PrintExtremes(history);
    PrintSociety(history);
    failures += PopulationCheck(history, data);
    failures += GrowthCheck(history);
    failures += RangeCheck(world);
    Console.WriteLine(failures == 0 ? "Все проверки пройдены." : $"Нарушений: {failures}");
    return failures == 0 ? 0 : 1;
}

// Разогрев не должен уводить показатели далеко от данных: в норме страны с 90% мирового ВВП и 80% стран.
static int WarmupCheck(WarmupReport report, bool strict)
{
    Console.WriteLine($"Разогрев {report.Months} мес.: в пределах 10% от данных — {report.CountryShareWithinTolerance:P0} стран, {report.GdpShareWithinTolerance:P0} мирового ВВП.");
    foreach (var (id, _, gated) in WarmupReport.Checked)
    {
        var items = report.Deviations.Where(d => d.Indicator == id).ToList();
        var ok = items.Count(d => d.WithinTolerance);
        var worst = items.OrderByDescending(d => Math.Abs(d.Warmed - d.Start)).Take(3)
            .Select(d => $"{d.Country} {d.Start:F1}→{d.Warmed:F1}");
        Console.WriteLine($"  {id,-26} в норме {ok,3} из {items.Count}; сильнее всего: {string.Join(", ", worst)}{(gated ? "" : " (только для сведения)")}");
    }
    var failed = report.GdpShareWithinTolerance < 0.9 || report.CountryShareWithinTolerance < 0.8;
    if (failed && strict)
        Console.Error.WriteLine("ОШИБКА разогрев уводит показатели от данных больше чем на 10%");
    return failed && strict ? 1 : 0;
}

// Население 10 крупнейших стран к 2050 году в пределах ±15% от прогноза ООН (критерий этапа 2).
static int PopulationCheck(List<(int Year, double WorldGdp, Dictionary<string, Dictionary<string, double>> Stats)> history, GameData data)
{
    const int year = 2050;
    var at = history.FirstOrDefault(h => h.Year == year);
    if (at.Stats is null || data.Benchmarks is null)
        return 0;
    var start = history[0];
    var top = start.Stats.OrderByDescending(kv => kv.Value["population"]).Take(10).Select(kv => kv.Key).ToList();
    Console.WriteLine();
    Console.WriteLine($"Население в {year} году: модель и прогноз ООН, млн человек");
    var failures = 0;
    double modelWorld = 0, unWorld = 0;
    foreach (var (id, years) in data.Benchmarks.Population)
    {
        if (!years.TryGetValue(year.ToString(), out var un) || !at.Stats.TryGetValue(id, out var s))
            continue;
        modelWorld += s["population"];
        unWorld += un;
    }
    foreach (var id in top)
    {
        if (!data.Benchmarks.Population.TryGetValue(id, out var years) || !years.TryGetValue(year.ToString(), out var un))
            continue;
        var model = at.Stats[id]["population"];
        var deviation = model / un - 1;
        var ok = Math.Abs(deviation) <= 0.15;
        if (!ok)
            failures++;
        Console.WriteLine($"  {id}  модель {model / 1e6,7:F0}  ООН {un / 1e6,7:F0}  отклонение {deviation,6:P1}{(ok ? "" : "  ВНЕ ±15%")}");
    }
    Console.WriteLine($"  Мир (страны с прогнозом): модель {modelWorld / 1e9:F2} млрд, ООН {unWorld / 1e9:F2} млрд, отклонение {modelWorld / unWorld - 1:P1}");
    if (failures > 0)
        Console.Error.WriteLine($"ОШИБКА население {failures} из 10 крупнейших стран к {year} году отличается от прогноза ООН больше чем на 15%");
    return failures > 0 ? 1 : 0;
}

// Мировой рост в среднем 1–5% в год (реальный ВВП в ценах и курсах старта).
static int GrowthCheck(List<(int Year, double WorldGdp, Dictionary<string, Dictionary<string, double>> Stats)> history)
{
    var first = history[0];
    var last = history[^1];
    var span = last.Year - first.Year;
    if (span <= 0)
        return 0;
    var average = Math.Pow(last.WorldGdp / first.WorldGdp, 1.0 / span) - 1;
    Console.WriteLine($"Мировой рост в среднем за {span} лет: {average:P2} в год.");
    if (average is >= 0.01 and <= 0.05)
        return 0;
    Console.Error.WriteLine("ОШИБКА мировой рост вне 1–5% в год");
    return 1;
}

// Диапазоны те же, что для входных данных; суммы в долларах сравниваются в ценах старта (без инфляции доллара).
static int RangeCheck(World world)
{
    var failures = 0;
    var dollar = Politeconomy.Sim.Economy.Systems.EconomySystem.Reference(world.Economy!).Price;
    string[] money = ["gdp_usd", "gdp_per_capita_usd", "gdp_ppp_per_capita"];
    foreach (var country in world.Countries)
    {
        foreach (var (key, stat) in country.Stats)
        {
            var value = money.Contains(key) ? stat.Value / dollar : stat.Value;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                Console.Error.WriteLine($"ОШИБКА {country.Id}.{key}: значение не число");
                failures++;
            }
            else if (DataValidator.Ranges.TryGetValue(key, out var r) && (value < r.Min || value > r.Max))
            {
                Console.Error.WriteLine($"ОШИБКА {country.Id}.{key} = {value:F2} вне {r.Min}…{r.Max}");
                failures++;
            }
        }
    }
    Console.WriteLine(failures == 0 ? "Диапазоны показателей в норме, бесконечностей и NaN нет." : $"Выходов за диапазоны: {failures}");
    return failures;
}

static double WorldRealGdp(World world) =>
    world.Economy!.Countries.Values.Sum(e => e.RealGdp);

// Крайние значения за весь прогон — чтобы сразу видеть страны, где модель уходит в разнос.
static void PrintExtremes(List<(int Year, double WorldGdp, Dictionary<string, Dictionary<string, double>> Stats)> history)
{
    Console.WriteLine();
    var ids = history[0].Stats.Keys.ToList();
    void Top(string title, Func<string, double> score, string format, bool ascending = false)
    {
        var valid = ids.Select(id => (id, v: score(id))).Where(t => !double.IsNaN(t.v));
        var top = (ascending ? valid.OrderBy(t => t.v) : valid.OrderByDescending(t => t.v)).Take(6);
        Console.WriteLine($"{title}: {string.Join(", ", top.Select(t => $"{t.id} {t.v.ToString(format)}"))}");
    }
    double Max(string id, string key) => history.Max(h => h.Stats[id].GetValueOrDefault(key, double.NaN));
    double Min(string id, string key) => history.Min(h => h.Stats[id].GetValueOrDefault(key, double.NaN));
    double Growth(string id) =>
        Math.Pow(history[^1].Stats[id]["gdp_ppp_per_capita"] / history[0].Stats[id]["gdp_ppp_per_capita"], 1.0 / (history.Count - 1)) - 1;
    Top("Наибольший долг, % ВВП", id => Max(id, "gov_debt_pct_gdp"), "F0");
    Top("Наибольшая инфляция, %", id => Max(id, "inflation_pct"), "F0");
    Top("Наибольшая безработица, %", id => Max(id, "unemployment_pct"), "F0");
    Top("Наибольший дефицит, % ВВП", id => -Min(id, "budget_balance_pct_gdp"), "F0");
    Top("Самый глубокий спад, %", id => -Min(id, "gdp_growth_pct"), "F0");
    Top("Самая высокая ставка ЦБ, %", id => Max(id, "policy_rate_pct"), "F0");
    Top("Быстрее всех растёт ВВП на душу по ППС (в текущих долларах), в год", Growth, "P1");
    Top("Медленнее всех", Growth, "P1", ascending: true);
    Top("Дефолтов за прогон", id => history[^1].Stats[id].GetValueOrDefault("defaults", 0), "F0");
}

static void PrintWorld(List<(int Year, double WorldGdp, Dictionary<string, Dictionary<string, double>> Stats)> history)
{
    Console.WriteLine();
    Console.WriteLine("Год   Рост мира  Инфляция(медиана)  Безработица(медиана)  Долг(медиана)  Сальдо(медиана)");
    for (var i = 0; i < history.Count; i += Math.Max(1, history.Count / 12))
    {
        var h = history[i];
        var growth = i > 0 ? h.WorldGdp / history[i - 1].WorldGdp - 1 : double.NaN;
        double Median(string key)
        {
            var values = h.Stats.Values.Select(s => s.GetValueOrDefault(key, double.NaN)).Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
            return values.Count > 0 ? values[values.Count / 2] : double.NaN;
        }
        Console.WriteLine($"{h.Year}  {growth,8:P1}  {Median("inflation_pct"),12:F1}%  {Median("unemployment_pct"),16:F1}%  {Median("gov_debt_pct_gdp"),10:F1}%  {Median("budget_balance_pct_gdp"),12:F1}%");
    }
}

static void PrintCountry(List<(int Year, double WorldGdp, Dictionary<string, Dictionary<string, double>> Stats)> history, string id)
{
    Console.WriteLine();
    Console.WriteLine($"{id}: год, рост, инфляция, безработица, ставка ЦБ, долг, сальдо, тек. счёт, курс за год, рейтинг");
    for (var i = 0; i < history.Count; i += Math.Max(1, history.Count / 12))
    {
        var s = history[i].Stats[id];
        double V(string k) => s.GetValueOrDefault(k, double.NaN);
        Console.WriteLine($"  {history[i].Year} {V("gdp_growth_pct"),6:F1} {V("inflation_pct"),6:F1} {V("unemployment_pct"),5:F1} {V("policy_rate_pct"),6:F1} {V("gov_debt_pct_gdp"),6:F0} {V("budget_balance_pct_gdp"),6:F1} {V("current_account_pct_gdp"),6:F1} {V("exchange_rate_change_pct"),6:F1} {Fmt.Rating(V("credit_rating")),5}");
    }
}

// Настроение мира: средние по населению уровень жизни, удовлетворённость и радикализм; самые недовольные страны.
static void PrintSociety(List<(int Year, double WorldGdp, Dictionary<string, Dictionary<string, double>> Stats)> history)
{
    Console.WriteLine();
    Console.WriteLine("Год   Уровень жизни  Удовлетворённость  Радикализм  (средние по населению мира)");
    for (var i = 0; i < history.Count; i += Math.Max(1, history.Count / 12))
    {
        var h = history[i];
        double Mean(string key)
        {
            var weight = h.Stats.Values.Sum(s => s.GetValueOrDefault("population"));
            return h.Stats.Values.Sum(s => s.GetValueOrDefault(key) * s.GetValueOrDefault("population")) / weight;
        }
        Console.WriteLine($"{h.Year}  {Mean("standard_of_living"),12:F1}  {Mean("satisfaction"),16:F1}  {Mean("radicalism"),10:F1}");
    }
    var ids = history[0].Stats.Keys.ToList();
    var lowest = ids.Select(id => (id, v: history.Min(h => h.Stats[id].GetValueOrDefault("satisfaction", double.NaN)))).OrderBy(t => t.v).Take(6);
    var radical = ids.Select(id => (id, v: history.Max(h => h.Stats[id].GetValueOrDefault("radicalism", double.NaN)))).OrderByDescending(t => t.v).Take(6);
    Console.WriteLine($"Самая низкая удовлетворённость: {string.Join(", ", lowest.Select(t => $"{t.id} {t.v:F0}"))}");
    Console.WriteLine($"Самый высокий радикализм: {string.Join(", ", radical.Select(t => $"{t.id} {t.v:F0}"))}");
}

// Классы стран на старте и (с --years N) через N лет.
int Classes()
{
    var sim = GameSetup.NewGame(data).Simulation;
    var world = sim.World;
    var focus = Option("--country");
    string[] ids = focus is null ? ["USA", "DEU", "RUS", "CHN", "IND", "NGA", "BRA", "SAU", "JPN", "ETH"] : [focus];
    void Print()
    {
        foreach (var id in ids.Where(id => world.ById(id) is not null))
        {
            var s = world.Society![id];
            var c = world[id];
            Console.WriteLine();
            Console.WriteLine($"{id} {Fmt.Date(world.Date)}: ВВП на душу по ППС {c.Stat("gdp_ppp_per_capita"):F0}, уровень жизни {c.Stat("standard_of_living"):F0}, " +
                $"удовлетворённость {c.Stat("satisfaction"):F0}, радикализм {c.Stat("radicalism"):F0}, поправка доходов {s.Scale:F2}");
            Console.WriteLine("  класс                             доля%   доход   потр.  сбереж.  ур.жизни ожид. удовл. радик. влиян.%");
            for (var k = 0; k < s.Size.Length; k++)
                Console.WriteLine($"  {data.Classes.Classes[k].Name,-32} {s.Size[k] / s.Total * 100,6:F1} {s.Income[k],7:F0} {s.Consumption[k],7:F0} {s.Savings[k],8:F0} {s.StandardOfLiving[k],7:F1} {s.Expectations[k],6:F1} {s.Satisfaction[k],6:F1} {s.Radicalism[k],6:F1} {s.Influence[k],6:F1}");
        }
    }
    if (args.Contains("--scan"))
    {
        // Все страны: поправка доходов и разброс уровня жизни между классами.
        foreach (var country in world.Countries.OrderBy(c => world.Society![c.Id].Scale))
        {
            var s = world.Society![country.Id];
            var min = Enumerable.Range(0, s.Size.Length).Where(k => s.Size[k] > 0).Min(k => s.StandardOfLiving[k]);
            var max = Enumerable.Range(0, s.Size.Length).Where(k => s.Size[k] > 0).Max(k => s.StandardOfLiving[k]);
            Console.WriteLine($"{country.Id} поправка {s.Scale:F2} ур.жизни {country.Stat("standard_of_living"):F0} ({min:F0}…{max:F0}) удовл. {country.Stat("satisfaction"):F0} радик. {country.Stat("radicalism"):F0}");
        }
        return 0;
    }
    Print();
    var runYears = int.TryParse(Option("--years"), out var ry) ? ry : 0;
    if (runYears > 0)
    {
        sim.RunUntil(world.Date.AddYears(runYears));
        Print();
    }
    return 0;
}

// Правительство страны: глава, министры с качествами и чертами, парламент; с --years N — журнал за эти годы.
int Government()
{
    var sim = GameSetup.NewGame(data).Simulation;
    var world = sim.World;
    var runYears = int.TryParse(Option("--years"), out var ry) ? ry : 0;
    if (runYears > 0)
        sim.RunUntil(world.Date.AddYears(runYears));
    string[] ids = Option("--country") is { } one ? [one] : ["RUS", "USA", "CHN", "DEU", "IND", "BRA", "NGA", "SAU", "JPN", "VNM"];
    var traits = data.Government.Traits.ToDictionary(t => t.Id, t => t.Name);
    foreach (var id in ids)
    {
        var country = world[id];
        var g = world.Government![id];
        var p = g.Parliament;
        string Line(Character c) =>
            $"{c.Name} ({(c.Female ? "ж" : "м")}, {c.Age(world.Date)}) комп {c.Competence:F0} лоял {c.Loyalty:F0} корр {c.Corruption:F0} поп {c.Popularity:F0}" +
            (c.Traits.Count > 0 ? " — " + string.Join(", ", c.Traits.Select(t => traits[t])) : "");
        Console.WriteLine();
        Console.WriteLine($"{country.Name} ({WorldFactory.RegimeId(country.Regime)}): {Politeconomy.Sim.Government.GovernmentModel.LeaderTitle(data, country)} {Line(g.Leader)}");
        Console.WriteLine($"  {p.Name}: {p.Kind}, мест {p.Seats}, правящий блок {p.RulingSeats}, дисциплина {p.Discipline:F2}");
        foreach (var post in data.Government.Posts)
            Console.WriteLine($"  {post.Name,-36} {Line(g.Ministers[post.Id])}");
    }
    if (runYears > 0)
        foreach (var e in world.Journal.Where(e => ids.Contains(e.Country)).TakeLast(20))
            Console.WriteLine($"{e.Date:yyyy-MM-dd} {e.Country} {e.Text}");
    return 0;
}

// Трассировка одной страны: по умолчанию разогрев по месяцам (без толчков), с --years N — игра после разогрева.
int Trace()
{
    var id = Option("--country") ?? "USA";
    var months = int.TryParse(Option("--months"), out var m) ? m : 24;
    var gameYears = int.TryParse(Option("--years"), out var gy) ? gy : 0;
    World world;
    Simulation? sim = null;
    if (gameYears > 0)
    {
        sim = GameSetup.NewGame(data).Simulation;
        world = sim.World;
    }
    else
    {
        world = WorldFactory.Create(data);
        EconomyModel.Setup(world);
    }
    Console.WriteLine("мес   ВВПреал  ВВПном   разрыв  потенц  безраб  инфл   ожид   ставка  курс   долг%  доходы% расходы% проц%  сальдо%  тек.сч% r_eff  r_новые рейт");
    void Line(int month)
    {
        var e = world.Economy![id];
        var y = e.NominalGdp;
        Console.WriteLine($"{month,3} {e.RealGdp / e.RealGdp0,8:F3} {y / e.RealGdp0,7:F3} {e.Gap * 100,7:F2} {e.PotentialGrowth * 100,6:F2} {e.Unemployment * 100,6:F1} {e.Inflation * 100,6:F1} {e.Expectations * 100,6:F1} {e.PolicyRate * 100,6:F1} {e.ExchangeRate,6:F3} {e.DebtRatio * 100,6:F1} {e.TotalRevenue / y * 100,7:F1} {e.TotalSpending / y * 100,7:F1} {e.Interest / y * 100,5:F1} {e.BalanceRatio * 100,7:F1} {e.CurrentAccount * 100,7:F1} {e.EffectiveRate * 100,5:F1} {e.MarketRate * 100,6:F1} {Fmt.Rating(e.Rating),5}");
        if (args.Contains("--detail"))
            Console.WriteLine($"     q={e.RealExchangeRate:F3} нефть={world.Economy!.OilPrice:F2} добыча/ВВП={e.ResourceGdp / y * 100:F1}% " +
                string.Join(" ", data.Budget.Revenues.Select((r, k) => $"{r.Id}={e.Revenue[k] / y * 100:F1}")) +
                $" население={e.Population / 1e6:F1}млн РС={e.LaborForce / 1e6:F1}млн");
    }
    Line(0);
    if (sim is not null)
    {
        for (var month = 1; month <= gameYears * 12; month++)
        {
            sim.RunUntil(world.Date.AddMonths(1));
            if (month % 6 == 0)
                Line(month);
        }
        return 0;
    }
    world.Economy!.Warmup = true;
    var systems = EconomyModel.Systems().Where(s => s.Frequency == SimFrequency.Monthly).ToList();
    for (var month = 1; month <= months; month++)
    {
        foreach (var system in systems)
            system.Run(world);
        if (month <= 3 || month % 3 == 0)
            Line(month);
    }
    return 0;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
