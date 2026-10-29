namespace Politeconomy.Sim.Data;

/// <summary>
/// История показателей стран до старта (data/history.json): по каждой стране и показателю — значения за годы
/// FirstYear…LastYear, null там, где данных нет.
/// </summary>
public sealed record HistoryFile(
    int FirstYear,
    int LastYear,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<double?>>> Countries);
