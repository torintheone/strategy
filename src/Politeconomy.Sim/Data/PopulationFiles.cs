namespace Politeconomy.Sim.Data;

// Демография из data/population.json и ориентиры для проверок из data/benchmarks/population.json.

public sealed record PopulationFile(
    string Built,
    IReadOnlyList<string> Groups,
    IReadOnlyList<PopulationRecord> Countries);

/// <summary>
/// Половозрастная структура страны (пятилетние группы 0–4 … 75–79 и 80+), рождаемость, смертность, миграция,
/// урбанизация и путь рождаемости и продолжительности жизни по прогнозу ООН (если он есть).
/// </summary>
public sealed record PopulationRecord(
    string Id,
    int Year,
    IReadOnlyList<double> Female,
    IReadOnlyList<double> Male,
    double Fertility,
    double AdolescentFertility,
    double LifeExpectancyFemale,
    double LifeExpectancyMale,
    double InfantMortality,
    double NetMigration,
    double UrbanPct,
    double UrbanChange,
    ProjectionPath? Path = null);

/// <summary>Прогноз по годам: суммарная рождаемость и продолжительность жизни (обоих полов).</summary>
public sealed record ProjectionPath(
    IReadOnlyList<int> Years,
    IReadOnlyList<double> Fertility,
    IReadOnlyList<double> LifeExpectancy);

public sealed record BenchmarksFile(
    string Source,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Population);
