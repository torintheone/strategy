namespace Politeconomy.Sim.Politics;

/// <summary>
/// Правление игрока: страна, с какой даты и показатели на начало (для экрана итогов), сколько выборов выиграно.
/// </summary>
public sealed class Reign
{
    public string Country { get; set; } = "";
    public DateOnly Since { get; set; }
    public Dictionary<string, double> Start { get; set; } = new(StringComparer.Ordinal);
    public int ElectionsWon { get; set; }
    public int ElectionsLost { get; set; }
    public int LawsPassed { get; set; }
}

/// <summary>Игрок проиграл выборы: кто победил. Ждёт решения игрока — продолжить за новое правительство или закончить.</summary>
public sealed record Defeat(DateOnly Date, string Country, string OldLeader, string NewLeader, string NewParty);

/// <summary>Итоги закончившегося правления: показатели на начало и на конец.</summary>
public sealed record ReignSummary(
    string Country,
    DateOnly Since,
    DateOnly Until,
    IReadOnlyDictionary<string, double> Start,
    IReadOnlyDictionary<string, double> End,
    int ElectionsWon,
    int ElectionsLost,
    int LawsPassed);
