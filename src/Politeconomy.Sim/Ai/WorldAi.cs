namespace Politeconomy.Sim.Ai;

/// <summary>ИИ правительств всех стран, кроме страны игрока. MODEL.md, «ИИ стран».</summary>
public sealed class WorldAi
{
    public SortedDictionary<string, CountryAi> Countries { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Сколько решений каждого вида принято за всю игру (журнал хранит только последние).</summary>
    public SortedDictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);

    public CountryAi this[string country] => Countries[country];
}

/// <summary>
/// ИИ одной страны: приоритеты (рост, стабильность, сила, переизбрание — доли, сумма 1) и почему они такие, прошлогодний
/// сдвиг статей бюджета (его ИИ снимает, прежде чем наложить новый), когда последний раз принималось решение каждого вида
/// (law, visit, treaty, sanction, bloc, council, fund) и журнал последних решений с причинами.
/// </summary>
public sealed class CountryAi
{
    public double[] Priorities { get; set; } = [0.25, 0.25, 0.25, 0.25];
    public List<string> Why { get; set; } = [];
    public double[] Tilt { get; set; } = [];
    public SortedDictionary<string, DateOnly> Last { get; set; } = new(StringComparer.Ordinal);
    public List<AiDecision> Decisions { get; set; } = [];

    public double Growth => Priorities[AiModel.Growth];
    public double Stability => Priorities[AiModel.Stability];
    public double Power => Priorities[AiModel.Power];
    public double Reelection => Priorities[AiModel.Reelection];

    /// <summary>Прошло ли months месяцев с последнего решения вида kind.</summary>
    public bool Ready(string kind, DateOnly date, int months) => !Last.TryGetValue(kind, out var last) || last.AddMonths(months) <= date;
}

/// <summary>Решение ИИ: когда, какого вида (budget, law, diplomacy, sanction, bloc, council, fund), что сделано и почему.</summary>
public sealed record AiDecision(DateOnly Date, string Kind, string Text, string Why);
