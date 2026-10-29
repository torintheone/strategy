namespace Politeconomy.Sim.Data;

// Коэффициенты бюджетного процесса из data/balance.json (раздел budget_cycle). Смысл — в MODEL.md, «Бюджетный цикл».

public sealed class BudgetCycleBalance
{
    /// <summary>Месяц, когда минфин вносит проект на следующий год, месяц голосования и последняя попытка.</summary>
    public int DraftMonth { get; init; } = 9;
    public int VoteMonth { get; init; } = 10;
    public int LastVoteMonth { get; init; } = 12;
    /// <summary>Отношение класса: IncomeAttitude за каждый процент изменения его дохода плюс SpendingAttitude × относительное изменение статей, за которыми он следит.</summary>
    public double IncomeAttitude { get; init; } = 1.0;
    public double SpendingAttitude { get; init; } = 2.0;
    public double AttitudeCap { get; init; } = 3;
    public double SupportScale { get; init; } = 1.0;
    /// <summary>Дефицит больше DeficitTolerance ВВП снижает одобрение на DeficitPenalty за каждую долю ВВП сверх.</summary>
    public double DeficitTolerance { get; init; } = 0.04;
    public double DeficitPenalty { get; init; } = 5;
    /// <summary>Пределы ползунков: множитель налога и расходы относительно нынешних.</summary>
    public double TaxMin { get; init; } = 0.5;
    public double TaxMax { get; init; } = 1.6;
    public double SpendingMax { get; init; } = 2.5;
}
