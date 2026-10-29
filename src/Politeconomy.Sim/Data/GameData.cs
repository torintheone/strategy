using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Politeconomy.Sim.Data;

/// <summary>Всё содержимое папки data/, прочитанное с диска. Не меняется во время партии.</summary>
public sealed class GameData
{
    public required string Directory { get; init; }
    public required CountriesFile Countries { get; init; }
    public required RegionsFile Regions { get; init; }
    public required ControlFile Control { get; init; }
    public required PoliticsFile Politics { get; init; }
    public required ScenarioFile Scenario { get; init; }
    public required BalanceFile Balance { get; init; }
    public required IndicatorsFile Indicators { get; init; }
    public required MapModesFile MapModes { get; init; }
    public required WorldMapFile Map { get; init; }
    public required CitiesFile Cities { get; init; }
    public required CurrenciesFile Currencies { get; init; }
    public required IndustriesFile Industries { get; init; }
    public required BudgetFile Budget { get; init; }
    public required PopulationFile Population { get; init; }
    public required ClassesFile Classes { get; init; }
    public required GovernmentFile Government { get; init; }
    public required LawsFile Laws { get; init; }
    /// <summary>История показателей за 10 лет до старта (для графиков ведомств).</summary>
    public required HistoryFile History { get; init; }
    /// <summary>Культуры имён из папки data/names/, по одной на файл.</summary>
    public required IReadOnlyList<CultureRecord> Cultures { get; init; }
    /// <summary>Ориентиры для проверок правдоподобия (прогноз ООН); в игре не используются.</summary>
    public BenchmarksFile? Benchmarks { get; init; }

    /// <summary>Коэффициенты модели экономики; если раздела нет в balance.json — значения по умолчанию.</summary>
    public EconomyBalance Economy => Balance.Economy ?? DefaultEconomy;

    static readonly EconomyBalance DefaultEconomy = new();

    /// <summary>Коэффициенты демографии; если раздела нет в balance.json — значения по умолчанию.</summary>
    public DemographyBalance Demography => Balance.Demography ?? DefaultDemography;

    static readonly DemographyBalance DefaultDemography = new();

    /// <summary>Коэффициенты классов; если раздела нет в balance.json — значения по умолчанию.</summary>
    public SocietyBalance Society => Balance.Society ?? DefaultSociety;

    static readonly SocietyBalance DefaultSociety = new();

    /// <summary>Коэффициенты правительства и парламента; если раздела нет в balance.json — значения по умолчанию.</summary>
    public GovernmentBalance GovernmentBalance => Balance.Government ?? DefaultGovernment;

    static readonly GovernmentBalance DefaultGovernment = new();

    /// <summary>Коэффициенты законов и институтов; если раздела нет в balance.json — значения по умолчанию.</summary>
    public LawsBalance LawsBalance => Balance.Laws ?? DefaultLaws;

    static readonly LawsBalance DefaultLaws = new();

    /// <summary>Коэффициенты бюджетного процесса; если раздела нет в balance.json — значения по умолчанию.</summary>
    public BudgetCycleBalance BudgetCycleBalance => Balance.BudgetCycle ?? DefaultBudgetCycle;

    static readonly BudgetCycleBalance DefaultBudgetCycle = new();

    public LawRecord Law(string id) =>
        Laws.Laws.FirstOrDefault(l => l.Id == id) ?? throw new KeyNotFoundException($"Нет закона «{id}» в data/laws.json");

    public IndicatorDef Indicator(string id) =>
        Indicators.Indicators.FirstOrDefault(i => i.Id == id)
        ?? throw new KeyNotFoundException($"Нет показателя «{id}» в data/indicators.json");

    /// <summary>Читает все файлы. Бросает <see cref="DataException"/> с понятным текстом, если файл не читается.</summary>
    public static GameData Load(string directory)
    {
        var ctx = DataJsonContext.Default;
        return new GameData
        {
            Directory = directory,
            Countries = Read(directory, "countries.json", ctx.CountriesFile),
            Regions = Read(directory, "regions.json", ctx.RegionsFile),
            Control = Read(directory, "control.json", ctx.ControlFile),
            Politics = Read(directory, "politics.json", ctx.PoliticsFile),
            Scenario = Read(directory, "scenario.json", ctx.ScenarioFile),
            Balance = Read(directory, "balance.json", ctx.BalanceFile),
            Indicators = Read(directory, "indicators.json", ctx.IndicatorsFile),
            MapModes = Read(directory, "map_modes.json", ctx.MapModesFile),
            Map = Read(directory, Path.Combine("map", "world.json"), ctx.WorldMapFile),
            Cities = Read(directory, Path.Combine("map", "cities.json"), ctx.CitiesFile),
            Currencies = Read(directory, "currencies.json", ctx.CurrenciesFile),
            Industries = Read(directory, "industries.json", ctx.IndustriesFile),
            Budget = Read(directory, "budget.json", ctx.BudgetFile),
            Population = Read(directory, "population.json", ctx.PopulationFile),
            Classes = Read(directory, "classes.json", ctx.ClassesFile),
            Government = Read(directory, "government.json", ctx.GovernmentFile),
            Laws = Read(directory, "laws.json", ctx.LawsFile),
            History = Read(directory, "history.json", ctx.HistoryFile),
            Cultures = ReadCultures(directory),
            Benchmarks = File.Exists(Path.Combine(directory, "benchmarks", "population.json"))
                ? Read(directory, Path.Combine("benchmarks", "population.json"), ctx.BenchmarksFile)
                : null,
        };
    }

    static IReadOnlyList<CultureRecord> ReadCultures(string directory)
    {
        var folder = Path.Combine(directory, "names");
        if (!System.IO.Directory.Exists(folder))
            throw new DataException($"Не найдена папка с именами {folder}");
        return System.IO.Directory.GetFiles(folder, "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => Read(directory, Path.Combine("names", Path.GetFileName(f)), DataJsonContext.Default.CultureRecord))
            .ToList();
    }

    static T Read<T>(string directory, string file, JsonTypeInfo<T> type)
    {
        var path = Path.Combine(directory, file);
        if (!File.Exists(path))
            throw new DataException($"Не найден файл данных {path}");
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, type)
                ?? throw new DataException($"Файл {file} пустой");
        }
        catch (JsonException e)
        {
            throw new DataException($"Ошибка в файле {file}: {e.Message}", e);
        }
    }
}

public sealed class DataException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Поиск папки data/ рядом с программой или выше по дереву каталогов.</summary>
public static class DataDirectory
{
    public const string MarkerFile = "countries.json";

    public static string? Find(params string[] startPoints)
    {
        foreach (var start in startPoints)
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "data");
                if (File.Exists(Path.Combine(candidate, MarkerFile)))
                    return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }
}
