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
//   check [--years N] [--random-laws] — прогон и проверки правдоподобия (по умолчанию 50 лет):
//                         разогрев не уводит показатели от данных, мировой рост 1–5% в год, нет NaN и выходов за диапазоны;
//                         с --random-laws у каждой страны случайные варианты всех законов (проверка устойчивости модели)
//   report [--years N] [--country ISO] — таблица показателей стран по годам
//   classes [--years N] [--country ISO] — классы стран: численность, доходы, уровень жизни, настроение, влияние
//   government [--country ISO] [--years N] — правительство и парламент страны, журнал отставок
//   ministry [--country ISO] [--years N] — доклады всех министров страны (как на экране «Ведомства»)
//   politics [--country ISO] [--years N] — партии, опросы, места в парламенте и взгляды классов
// Общий ключ: --data <папка> — где лежит data/ (по умолчанию ищется рядом и выше).

var command = args.FirstOrDefault() ?? "help";
var dataDir = Option("--data") ?? DataDirectory.Find(Environment.CurrentDirectory, AppContext.BaseDirectory);
var years = int.TryParse(Option("--years"), out var y) ? y : 50;

if (command is "help" or "--help" or "-h")
{
    Console.WriteLine("Politeconomy.Tools validate | run [--years N] | check [--years N] | report [--years N] [--country ISO] | classes [--years N] [--country ISO] | government [--country ISO] | ministry [--country ISO] [--years N] | politics [--country ISO] [--years N] | campaign [--country ISO] [--count N] [--data <папка>]");
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
    case "ministry":
        return Ministry();
    case "politics":
        return PoliticsReport();
    case "campaign":
        return CampaignReport();
    case "groups":
        return GroupsReport();
    case "protests":
        return ProtestsReport();
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
    if (args.Contains("--random-laws"))
    {
        var random = new DeterministicRandom(12345);
        foreach (var country in world.Countries)
        {
            var laws = world.Laws![country.Id];
            foreach (var law in data.Laws.Laws)
                laws.Current[law.Id] = law.Variants[random.Next(0, law.Variants.Count)].Id;
            Politeconomy.Sim.Laws.LawModel.Recompute(data, laws, world.Date);
        }
        Console.WriteLine("У всех стран случайные варианты законов.");
    }
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

    // Все выборы за прогон (у страны хранятся только последние, поэтому собираем по ходу).
    var elections = new List<(string Country, Regime Regime, Politeconomy.Sim.Politics.ElectionResult Result)>();
    var seen = new HashSet<Politeconomy.Sim.Politics.ElectionResult>(world.Politics!.Countries.Values.SelectMany(p => p.Elections));

    // Строй и режим стран на старте — чтобы увидеть, сколько их сменилось без выборов.
    var startRegimes = world.Countries.ToDictionary(c => c.Id, c => (c.System, c.Regime));
    // Протесты по месяцам: сколько стран-месяцев с массовыми протестами и с восстаниями.
    var unrest = (Months: 0, Any: 0, Mass: 0, Uprising: 0);
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
        if (world.Date.Day == 1)
            foreach (var country in world.Countries)
            {
                var p = world.Politics!.Countries[country.Id];
                foreach (var e in p.Elections.Where(seen.Add))
                    elections.Add((country.Id, country.Regime, e));
                unrest = (unrest.Months + 1, unrest.Any + (p.Protests.Count > 0 ? 1 : 0), unrest.Mass + (p.Protests.Any(x => x.Stage >= 2) ? 1 : 0),
                    unrest.Uprising + (p.Protests.Any(x => x.Stage >= 4) ? 1 : 0));
            }
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
    failures += ElectionCheck(elections, years);
    failures += GroupsCheck(world, data);
    failures += ProtestsCheck(unrest);
    failures += PowerCheck(world, startRegimes);
    Console.WriteLine(failures == 0 ? "Все проверки пройдены." : $"Нарушений: {failures}");
    return failures == 0 ? 0 : 1;
}

// Смена власти без выборов за прогон: сколько каких событий; режимы не должны массово рушиться.
static int PowerCheck(World world, Dictionary<string, (PoliticalSystem System, Regime Regime)> start)
{
    var changes = world.Politics!.Countries.SelectMany(kv => kv.Value.Changes.Select(c => (Country: kv.Key, Change: c))).ToList();
    var kinds = changes.GroupBy(x => x.Change.Kind).ToDictionary(g => g.Key, g => g.Count());
    string[] order = ["no_confidence", "impeachment", "palace_coup", "military_coup", "handover", "revolution"];
    string[] names = ["вотумов недоверия", "импичментов", "дворцовых переворотов", "военных переворотов", "передач власти хунтой", "революций"];
    Console.WriteLine("Смена власти без выборов: " + string.Join(", ", order.Select((k, i) => $"{names[i]} {kinds.GetValueOrDefault(k)}")) + ".");
    var military = world.Countries.Count(c => c.System == PoliticalSystem.MilitaryRegime);
    var militaryStart = start.Values.Count(x => x.System == PoliticalSystem.MilitaryRegime);
    var democracies = world.Countries.Count(c => c.Regime == Regime.Democracy);
    var democraciesStart = start.Values.Count(x => x.Regime == Regime.Democracy);
    Console.WriteLine($"  военных режимов: было {militaryStart}, стало {military}; демократий: было {democraciesStart}, стало {democracies}. " +
                      "Чаще всего: " + string.Join(", ", changes.GroupBy(x => x.Country).OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key} {g.Count()}")));
    var failures = 0;
    if (military > militaryStart + 25 || democracies < democraciesStart * 0.8 || kinds.GetValueOrDefault("military_coup") > 150)
    {
        Console.WriteLine("  НАРУШЕНИЕ: перевороты слишком часты");
        failures++;
    }
    return failures;
}

// Протесты: массовые идут в 0,1–15% стран-месяцев (в кризисных странах), восстания — не больше 2%.
static int ProtestsCheck((int Months, int Any, int Mass, int Uprising) u)
{
    double Share(int n) => u.Months > 0 ? (double)n / u.Months : 0;
    Console.WriteLine($"Протесты: хоть какие-то — в {Share(u.Any):P1} стран-месяцев, массовые и сильнее — в {Share(u.Mass):P1}, восстания — в {Share(u.Uprising):P2}.");
    if (Share(u.Mass) is < 0.001 or > 0.15 || Share(u.Uprising) > 0.02)
    {
        Console.WriteLine("  НАРУШЕНИЕ: протестов слишком мало или слишком много");
        return 1;
    }
    return 0;
}

// Группы интересов в конце прогона: одобрение не сваливается в крайности (враждебных — меньше четверти, у групп
// в среднем 35–65), давление групп на рост и ставки умеренное.
static int GroupsCheck(World world, GameData data)
{
    var approvals = world.Politics!.Countries.Values.SelectMany(p => p.Groups.Select(g => g.Approval)).ToList();
    var hostile = approvals.Count(a => a < 25) / (double)approvals.Count;
    var happy = approvals.Count(a => a > 75) / (double)approvals.Count;
    var growth = world.Politics.Countries.Values.Select(p => p.GroupEffects.GetValueOrDefault("potential_growth")).ToList();
    var spread = world.Politics.Countries.Values.Select(p => p.GroupEffects.GetValueOrDefault("spread")).ToList();
    var byGroup = data.InterestGroups.Groups.Select((r, i) => $"{r.Name} {world.Politics.Countries.Values.Where(p => p.Groups.Count > i).Average(p => p.Groups[i].Approval):F0}");
    Console.WriteLine($"Группы интересов: одобрение в среднем {approvals.Average():F1}, враждебных (<25) {hostile:P0}, восторженных (>75) {happy:P0}; " +
                      $"давление на рост от {growth.Min():F2} до {growth.Max():F2} п. п., на ставки от {spread.Min():F2} до {spread.Max():F2} п. п.");
    Console.WriteLine("  по группам: " + string.Join(", ", byGroup));
    var failures = 0;
    if (hostile > 0.25 || happy > 0.25)
    {
        Console.WriteLine("  НАРУШЕНИЕ: слишком много групп в крайностях");
        failures++;
    }
    var means = data.InterestGroups.Groups.Select((_, i) => world.Politics.Countries.Values.Where(p => p.Groups.Count > i).Average(p => p.Groups[i].Approval)).ToList();
    if (means.Any(m => m is < 35 or > 65))
    {
        Console.WriteLine("  НАРУШЕНИЕ: у какой-то группы среднее одобрение по миру вне 35–65");
        failures++;
    }
    if (growth.Min() < -1.5 || spread.Max() > 3)
    {
        Console.WriteLine("  НАРУШЕНИЕ: давление групп слишком сильное");
        failures++;
    }
    return failures;
}

// Выборы правдоподобны (критерий этапа 4): в демократиях власть иногда меняется, но не всегда; явка в разумных
// пределах; на выборах парламента ни одна партия в демократии не набирает больше 70%.
static int ElectionCheck(List<(string Country, Regime Regime, Politeconomy.Sim.Politics.ElectionResult Result)> elections, int years)
{
    if (elections.Count == 0)
        return 0;
    Console.WriteLine();
    var democratic = elections.Where(e => e.Regime == Regime.Democracy).ToList();
    var changes = democratic.Count(e => e.Result.PowerChanged);
    var changeShare = democratic.Count > 0 ? (double)changes / democratic.Count : 0;
    var turnouts = elections.Select(e => e.Result.Turnout).OrderBy(t => t).ToList();
    var landslides = democratic.Where(e => e.Result.Kind == "parliament" && e.Result.Votes.Values.DefaultIfEmpty(0).Max() > 0.7).ToList();
    var dictators = elections.Where(e => e.Regime == Regime.Authoritarian && e.Result.PowerChanged).ToList();
    Console.WriteLine($"Выборы за {years} лет: {elections.Count}, в демократиях {democratic.Count}; власть сменилась в {changeShare:P0} демократических выборов " +
                      $"(норма 15–70%); явка от {turnouts[0]:P0} до {turnouts[^1]:P0}, медиана {turnouts[turnouts.Count / 2]:P0}; " +
                      $"разгромов больше 70% в демократиях: {landslides.Count}; смен власти на выборах при авторитарном режиме: {dictators.Count}.");
    var failures = 0;
    void Fail(string text)
    {
        Console.Error.WriteLine("ОШИБКА " + text);
        failures++;
    }
    if (democratic.Count > 0 && changeShare is < 0.15 or > 0.7)
        Fail($"в демократиях власть меняется в {changeShare:P0} выборов — вне 15…70%");
    if (turnouts[0] < 0.25 || turnouts[^1] > 0.95)
        Fail($"явка от {turnouts[0]:P0} до {turnouts[^1]:P0} — вне 25…95%");
    if (landslides.Count > democratic.Count / 50)
        Fail($"в демократиях слишком много разгромов: {string.Join(", ", landslides.Take(5).Select(e => $"{e.Country} {e.Result.Date:yyyy}"))}");
    return failures;
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

int Ministry()
{
    var sim = GameSetup.NewGame(data).Simulation;
    var runYears = int.TryParse(Option("--years"), out var ry) ? ry : 0;
    if (runYears > 0)
        sim.RunUntil(sim.World.Date.AddYears(runYears));
    var id = Option("--country") ?? "BRA";
    var snapshot = WorldSnapshot.Capture(sim.World, 1, true, 1);
    var country = snapshot.Countries[id];
    Console.WriteLine($"{sim.World[id].Name}, {snapshot.Date:yyyy-MM-dd}");
    foreach (var post in data.Government.Posts)
    {
        var minister = country.Government?.Minister(post.Id);
        Console.WriteLine();
        Console.WriteLine($"{post.Ministry} — {minister?.Name}");
        foreach (var line in MinistryReport.Lines(data, post, country, snapshot, minister))
            Console.WriteLine("  • " + line);
        foreach (var stat in post.Stats ?? [])
        {
            var series = country.History?.Series(stat) ?? [];
            Console.WriteLine($"    {stat}: " + string.Join(" ", series.TakeLast(10).Select(p => $"{p.Year % 100:D2}:{p.Value:G4}")));
        }
    }
    return 0;
}

// Протесты: сколько их по стадиям каждый год, где беспокойнее всего, как отвечали власти.
int ProtestsReport()
{
    var sim = GameSetup.NewGame(data).Simulation;
    var world = sim.World;
    var runYears = int.TryParse(Option("--years"), out var ry) ? ry : 10;
    var months = new Dictionary<string, int>();
    var mass = new Dictionary<string, int>();
    for (var year = 0; year < runYears; year++)
    {
        var stages = new int[5];
        for (var m = 0; m < 12; m++)
        {
            sim.RunUntil(world.Date.AddMonths(1));
            foreach (var (id, p) in world.Politics!.Countries)
            {
                foreach (var x in p.Protests)
                    stages[Math.Clamp(x.Stage, 0, 4)]++;
                if (p.Protests.Count > 0)
                    months[id] = months.GetValueOrDefault(id) + 1;
                if (p.Protests.Any(x => x.Stage >= 2))
                    mass[id] = mass.GetValueOrDefault(id) + 1;
            }
        }
        Console.WriteLine($"{world.Date.Year - 1}: протестов в среднем за месяц — забастовки {stages[0] / 12.0:F1}, митинги {stages[1] / 12.0:F1}, " +
                          $"массовые {stages[2] / 12.0:F1}, беспорядки {stages[3] / 12.0:F1}, восстания {stages[4] / 12.0:F1}");
    }
    Console.WriteLine("Дольше всех протестуют (месяцев с протестом, из них с массовыми): " +
                      string.Join(", ", months.OrderByDescending(kv => kv.Value).Take(15).Select(kv => $"{kv.Key} {kv.Value}/{mass.GetValueOrDefault(kv.Key)}")));
    foreach (var id in new[] { "DEU", "USA", "FRA", "BRA", "RUS", "IRN", "VEN", "SDN" })
        Console.WriteLine($"  {world[id].Name}: протестов {months.GetValueOrDefault(id)} мес., массовых {mass.GetValueOrDefault(id)} мес.; " +
                          $"радикализм {world[id].Stat("radicalism"):F0}, лояльность полиции {world.Politics![id].Security.PoliceLoyalty:F0}, " +
                          $"репрессии {world.Politics![id].Security.Repression:F0}");
    return 0;
}

// Группы интересов: влияние, одобрение и его составляющие, давление и требования в нескольких странах.
int GroupsReport()
{
    var sim = GameSetup.NewGame(data).Simulation;
    var world = sim.World;
    var runYears = int.TryParse(Option("--years"), out var ry) ? ry : 0;
    if (runYears > 0)
        sim.RunUntil(world.Date.AddYears(runYears));
    string[] ids = Option("--country") is { } one ? [one] : ["USA", "GBR", "DEU", "FRA", "BRA", "IND", "RUS", "CHN", "SAU", "NGA", "IRN", "MMR"];
    var records = data.InterestGroups.Groups;
    foreach (var id in ids)
    {
        var p = world.Politics![id];
        Console.WriteLine();
        Console.WriteLine($"{world[id].Name} ({world[id].Regime}, {world[id].System}), религиозность {Politeconomy.Sim.Politics.InterestGroupsModel.Religiosity(data, world[id]):F2}");
        for (var i = 0; i < p.Groups.Count; i++)
        {
            var g = p.Groups[i];
            Console.WriteLine($"  {records[i].Name,-24} влияние {g.Influence * 100,5:F1}%  одобрение {g.Approval,5:F1}  " +
                              $"[классы {g.FromClasses,5:F1} законы {g.FromLaws,5:F1} бюджет {g.FromBudget,5:F1} власть {g.FromGovernment,5:F1} требования {g.Memory,4:F1}]  " +
                              string.Join("; ", g.Demands.Select(d => Politeconomy.Sim.Politics.InterestGroupsModel.Describe(data, d))));
        }
        if (p.GroupEffects.Count > 0)
            Console.WriteLine("  Давление: " + string.Join(", ", p.GroupEffects.Select(kv => $"{kv.Key} {kv.Value:+0.00;-0.00}")));
        if (p.Parties.Count > 0)
            Console.WriteLine("  Деньги партиям: " + string.Join(", ", p.Parties.Where(x => Math.Abs(x.Backing) > 0.001).Select(x => $"{x.Short} {x.Backing:+0.000}")));
    }
    return 0;
}

// Кампания в цифрах: в демократиях с выборами в первый год игры — доля партии главы без кампании, с активной кампанией
// (все очки на поездки и агитацию) и с фальсификациями; раскрыты ли они.
int CampaignReport()
{
    var start = GameSetup.NewGame(data).Simulation.World;
    var ids = Option("--country") is { } one ? [one] : start.Countries
        .Where(c => c.Regime == Regime.Democracy && start.Politics![c.Id].NextElection is { } d && d < start.Date.AddYears(1) && d > start.Date.AddMonths(3)
                    && start.Government![c.Id].Parliament.Exists)
        .Take(int.TryParse(Option("--count"), out var n) ? n : 10).Select(c => c.Id).ToList();
    Console.WriteLine($"{"Страна",-22} {"выборы",-10} {"пассивно",9} {"кампания",9} {"вбросы",9} {"массовые",9}  разоблачение");
    var gains = new List<double>();
    foreach (var id in ids)
    {
        var election = start.Politics![id].NextElection!.Value;
        double Votes(int mode, out bool exposed)
        {
            var sim = GameSetup.NewGame(data).Simulation;
            var world = sim.World;
            Politeconomy.Sim.Politics.PoliticsActions.PlayAs(world, id);
            var party = Politeconomy.Sim.Politics.PoliticsModel.HeadParty(world, id)!.Id;
            for (var date = world.Date.AddMonths(1); date <= election.AddDays(1); date = date.AddMonths(1))
            {
                sim.RunUntil(date);
                if (mode == 1 && Politeconomy.Sim.Politics.CampaignModel.Estimate(world, world[id]) is { } shown)
                    Console.WriteLine($"  {date:yyyy-MM}: обещания " + string.Join(", ", shown.Options.Take(3).Select(o => $"{o.Law}={o.Variant} {o.Gain * 100:+0.0;-0.0}"))
                                      + (shown.DebateGain.Length == 3 ? "; дебаты " + string.Join(" ", shown.DebateGain.Select(g => (g * 100).ToString("+0.0;-0.0"))) : ""));
                if (mode == 1)
                    while (Politeconomy.Sim.Politics.CampaignModel.Estimate(world, world[id]) is { } o)
                    {
                        var best = Array.IndexOf(o.TripGain, o.TripGain.Max());
                        if (!(o.TripGain[best] > 0 && Politeconomy.Sim.Politics.PoliticsActions.CampaignTrip(world, id, best))
                            && !Politeconomy.Sim.Politics.PoliticsActions.CampaignMedia(world, id))
                            break;
                    }
                if (mode >= 2)
                    Politeconomy.Sim.Politics.PoliticsActions.SetFraud(world, id, mode - 1);
            }
            sim.RunUntil(election.AddDays(1));
            exposed = world.Politics![id].Scandal > 0;
            return world.Politics![id].Elections.Last(e => e.Kind == "parliament").Votes.GetValueOrDefault(party);
        }
        var passive = Votes(0, out _);
        var active = Votes(1, out _);
        var light = Votes(2, out var lightExposed);
        var mass = Votes(3, out var massExposed);
        gains.Add(active - passive);
        Console.WriteLine($"{start[id].Name,-22} {election:yyyy-MM}    {passive * 100,8:F1}% {active * 100,8:F1}% {light * 100,8:F1}% {mass * 100,8:F1}%  " +
                          $"{(lightExposed ? "вбросы" : "")} {(massExposed ? "массовые" : "")}");
    }
    Console.WriteLine($"Кампания прибавляет в среднем {gains.Average() * 100:F1} п. п. (от {gains.Min() * 100:F1} до {gains.Max() * 100:F1})");
    return 0;
}

int PoliticsReport()
{
    var sim = GameSetup.NewGame(data).Simulation;
    var world = sim.World;
    var runYears = int.TryParse(Option("--years"), out var ry) ? ry : 0;
    if (runYears > 0)
        sim.RunUntil(world.Date.AddYears(runYears));
    string[] ids = Option("--country") is { } one ? [one] : ["USA", "GBR", "DEU", "FRA", "BRA", "IND", "JPN", "RUS", "CHN", "ZAF", "MEX", "SWE"];
    var classes = data.Classes.Classes;
    foreach (var id in ids)
    {
        var p = world.Politics![id];
        var g = world.Government![id];
        var record = data.PoliticsOf(id);
        Console.WriteLine();
        Console.WriteLine($"{world[id].Name}: {record.Electoral}, барьер {record.Threshold}%, {g.Parliament.Name} {g.Parliament.Seats} мест, " +
                          $"правящие {g.Parliament.RulingSeats}; глава {g.Leader.Name} ({p.Party(g.Leader.Party)?.Short ?? "—"})");
        foreach (var party in p.Parties.OrderByDescending(x => x.Poll))
        {
            var seats = g.Parliament.Factions.FirstOrDefault(f => f.Party == party.Id)?.Seats ?? 0;
            var top = Enumerable.Range(0, party.Voters.Length).OrderByDescending(k => party.Voters[k]).Take(3)
                .Select(k => $"{classes[k].ShortName ?? classes[k].Name} {party.Voters[k] * 100:F0}%");
            Console.WriteLine($"  {(party.Ruling ? "*" : " ")} {party.Name,-44} {party.Short,-14} {party.Family,-15} " +
                              $"[{party.Position[0],4:F0} {party.Position[1],4:F0} {party.Position[2],4:F0}] опрос {party.Poll * 100,5:F1}% мест {seats,4}  " +
                              string.Join(", ", top));
        }
        var ministers = g.Ministers.Values.GroupBy(m => p.Party(m.Party)?.Short ?? "беспарт.").Select(x => $"{x.Key} {x.Count()}");
        Console.WriteLine("  министры: " + string.Join(", ", ministers));
        Console.WriteLine("  взгляды классов: " + string.Join("; ", p.Views.Select((v, k) =>
            $"{classes[k].ShortName ?? classes[k].Name} [{v[0]:F0} {v[1]:F0} {v[2]:F0}]")));
        Console.WriteLine($"  выборы: парламент {p.NextElection:yyyy-MM}" + (p.NextPresident is { } next ? $", президент {next:yyyy-MM}" : "") +
                          $"; сроков у главы {g.Leader.Terms}");
    }
    if (runYears > 0)
        foreach (var e in world.Journal.Where(e => ids.Contains(e.Country) && e.Kind is "election" or "coalition" or "leader"))
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
