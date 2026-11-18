using System.Runtime.CompilerServices;
using Politeconomy.Sim.History;

namespace Politeconomy.Sim.Engine;

/// <summary>История показателей страны в снимке: значения за годы FirstYear…LastYear (null — нет данных).</summary>
public sealed record HistorySnapshot(int FirstYear, int LastYear, IReadOnlyDictionary<string, IReadOnlyList<double?>> Values)
{
    /// <summary>Значения показателя по годам (пары «год — значение» без пропусков).</summary>
    public IReadOnlyList<(int Year, double Value)> Series(string stat)
    {
        if (!Values.TryGetValue(stat, out var row))
            return [];
        var list = new List<(int, double)>(row.Count);
        for (var i = 0; i < row.Count; i++)
            if (row[i] is { } v)
                list.Add((FirstYear + i, v));
        return list;
    }

    static readonly ConditionalWeakTable<Dictionary<string, List<double?>>, Cached> Cache = new();

    sealed record Cached(int LastYear, int FirstYear, HistorySnapshot Snapshot);

    public static HistorySnapshot Capture(WorldHistory history, Dictionary<string, List<double?>> rows)
    {
        if (Cache.TryGetValue(rows, out var cached) && cached.LastYear == history.LastYear && cached.FirstYear == history.FirstYear)
            return cached.Snapshot;
        var snapshot = new HistorySnapshot(history.FirstYear, history.LastYear,
            rows.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double?>)kv.Value.ToArray(), StringComparer.Ordinal));
        Cache.AddOrUpdate(rows, new Cached(history.LastYear, history.FirstYear, snapshot));
        return snapshot;
    }
}
