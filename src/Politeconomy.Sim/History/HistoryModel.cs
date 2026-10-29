using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.History;

/// <summary>История показателей для графиков: данные до старта и записи модели каждый январь. MODEL.md, «История».</summary>
public static class HistoryModel
{
    /// <summary>Сколько лет истории хранится (старые годы отбрасываются).</summary>
    public const int Years = 30;

    public static IReadOnlyList<ISimSystem> Systems() => [new HistorySystem()];

    /// <summary>Какие показатели записываются: из данных и все, что показывают ведомства.</summary>
    public static IReadOnlyList<string> Tracked(Data.GameData data) =>
        data.History.Countries.Values.SelectMany(c => c.Keys)
            .Concat(data.Government.Posts.SelectMany(p => p.Stats ?? []))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

    public static void Setup(World world)
    {
        var data = world.Data.History;
        var history = new WorldHistory { FirstYear = data.FirstYear, LastYear = data.LastYear };
        var length = data.LastYear - data.FirstYear + 1;
        var tracked = Tracked(world.Data);
        foreach (var country in world.Countries)
        {
            var rows = new Dictionary<string, List<double?>>(StringComparer.Ordinal);
            var known = data.Countries.GetValueOrDefault(country.Id);
            foreach (var key in tracked)
            {
                var row = known?.GetValueOrDefault(key)?.ToList() ?? Enumerable.Repeat<double?>(null, length).ToList();
                while (row.Count < length)
                    row.Add(null);
                rows[key] = row;
            }
            history.Values[country.Id] = rows;
        }
        world.History = history;
    }

    /// <summary>Записать значения показателей за прошедший год (вызывается 1 января).</summary>
    public static void Record(World world)
    {
        var history = world.History ?? throw new InvalidOperationException("История не создана");
        var year = world.Date.Year - 1;
        if (year <= history.LastYear)
            return;
        foreach (var country in world.Countries)
        {
            var rows = history.Values[country.Id];
            foreach (var (key, row) in rows)
                row.Add(country.Stats.TryGetValue(key, out var v) && double.IsFinite(v.Value) ? Math.Round(v.Value, 4) : null);
        }
        history.LastYear = year;
        var extra = history.LastYear - history.FirstYear + 1 - Years;
        if (extra > 0)
        {
            foreach (var rows in history.Values.Values)
                foreach (var row in rows.Values)
                    row.RemoveRange(0, extra);
            history.FirstYear += extra;
        }
    }
}

/// <summary>1 января записывает значения показателей за прошедший год.</summary>
public sealed class HistorySystem : ISimSystem
{
    public string Name => "История показателей";
    public SimPhase Phase => SimPhase.Events;
    public SimFrequency Frequency => SimFrequency.Yearly;

    public void Run(World world) => HistoryModel.Record(world);
}
