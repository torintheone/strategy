using System.Diagnostics;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

// Консольные инструменты Politeconomy: проверка данных и прогоны мира без графики.
//   validate            — проверить data/*.json (ссылки, диапазоны, палитры)
//   run [--years N]     — прогнать мир N лет без графики и показать время шага
//   check [--years N]   — прогон и проверка показателей на правдоподобие (по умолчанию 50 лет)
// Общий ключ: --data <папка> — где лежит data/ (по умолчанию ищется рядом и выше).

var command = args.FirstOrDefault() ?? "help";
var dataDir = Option("--data") ?? DataDirectory.Find(Environment.CurrentDirectory, AppContext.BaseDirectory);
var years = int.TryParse(Option("--years"), out var y) ? y : 50;

if (command is "help" or "--help" or "-h")
{
    Console.WriteLine("Politeconomy.Tools validate | run [--years N] | check [--years N] [--data <папка>]");
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
    {
        var world = WorldFactory.Create(data);
        var sim = new Simulation(world);
        var end = world.Date.AddYears(years);
        var timer = Stopwatch.StartNew();
        var days = 0;
        var worstDayMs = 0.0;
        while (world.Date < end)
        {
            var t = timer.Elapsed.TotalMilliseconds;
            sim.StepDay();
            worstDayMs = Math.Max(worstDayMs, timer.Elapsed.TotalMilliseconds - t);
            days++;
        }
        Console.WriteLine($"Прогон: {years} лет ({days} дней) до {Fmt.Date(world.Date)} за {timer.Elapsed.TotalSeconds:F1} с; самый долгий день {worstDayMs:F2} мс.");
        if (command == "run")
            return 0;
        return Check(world);
    }
    default:
        Console.Error.WriteLine($"Неизвестная команда «{command}». Запустите с help.");
        return 2;
}

// Проверка правдоподобия: те же диапазоны, что и для входных данных.
static int Check(World world)
{
    var failures = 0;
    foreach (var country in world.Countries)
    {
        foreach (var (key, stat) in country.Stats)
        {
            if (double.IsNaN(stat.Value) || double.IsInfinity(stat.Value))
            {
                Console.Error.WriteLine($"ОШИБКА {country.Id}.{key}: значение не число");
                failures++;
            }
            else if (DataValidator.Ranges.TryGetValue(key, out var r) && (stat.Value < r.Min || stat.Value > r.Max))
            {
                Console.Error.WriteLine($"ОШИБКА {country.Id}.{key} = {stat.Value} вне {r.Min}…{r.Max}");
                failures++;
            }
        }
    }
    Console.WriteLine(failures == 0 ? "Проверка правдоподобия пройдена." : $"Нарушений: {failures}");
    return failures == 0 ? 0 : 1;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
