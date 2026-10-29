namespace Politeconomy.Sim.Politics;

/// <summary>
/// Предвыборная кампания партии игрока: к каким выборам, сколько очков у штаба, какие фальсификации заказаны
/// (0 — честные выборы, 1 — вбросы, 2 — массовые) и дебаты лидеров.
/// </summary>
public sealed class Campaign
{
    /// <summary>Партия, которая ведёт кампанию (партия главы на её начало).</summary>
    public string Party { get; set; } = "";
    public DateOnly Election { get; set; }
    public double Points { get; set; }
    public int Fraud { get; set; }
    public Debate? Debate { get; set; }
}

/// <summary>Дебаты лидеров: ось взглядов (тема) и ответ игрока (−1, 0, +1; null — ещё не выступал).</summary>
public sealed class Debate
{
    public DateOnly Date { get; set; }
    public int Axis { get; set; }
    public int? Answer { get; set; }
}

/// <summary>Обещание кампании: принять закон в таком-то варианте. Срок (Deadline) появляется, если партия победила.</summary>
public sealed class CampaignPromise
{
    public string Party { get; set; } = "";
    public string Law { get; set; } = "";
    public string Variant { get; set; } = "";
    public DateOnly? Deadline { get; set; }
}
