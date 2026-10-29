using Politeconomy.Sim.Government;

namespace Politeconomy.Sim.Budget;

/// <summary>
/// Бюджетный процесс страны игрока. Year — год, на который готовится проект (0 — проекта нет). Stage: none, draft
/// (проект внесён, ждёт голосования), rejected (отклонён, ждёт повторного голосования), passed (принят, вступит
/// в силу 1 января), failed (так и не принят — страна будет жить по прошлогоднему бюджету). Spending — расходы
/// проекта по статьям (доли ВВП), Taxes — множители к ставкам налогов. Amend* — поправки к текущему бюджету.
/// </summary>
public sealed class BudgetProcess
{
    public int Year { get; set; }
    public string Stage { get; set; } = "none";
    public double[] Spending { get; set; } = [];
    public double[] Taxes { get; set; } = [];
    public DateOnly? NextVote { get; set; }
    public List<VoteResult> Votes { get; set; } = [];
    public double[]? AmendSpending { get; set; }
    public double[]? AmendTaxes { get; set; }
    /// <summary>Когда поправки будут голосоваться (null — пока только черновик, не внесены).</summary>
    public DateOnly? AmendVote { get; set; }
    /// <summary>Прогноз для того, что сейчас редактируется (проект на следующий год или поправки).</summary>
    public BudgetForecast? Forecast { get; set; }
    public string? LastResult { get; set; }
    public int Version { get; set; }

    public bool HasDraft => Year > 0 && Stage is "draft" or "rejected";
}

/// <summary>
/// Прогноз бюджета: доходы, расходы без процентов, проценты и сальдо в долях ВВП; изменение дохода каждого класса
/// (доля), его отношение (−3…+3), одобрение в обществе и ожидаемое голосование.
/// </summary>
public sealed record BudgetForecast(
    double Revenue,
    double Spending,
    double Interest,
    double Balance,
    double[] IncomeChange,
    double[] Attitudes,
    double Support,
    VoteResult Vote);

public sealed class WorldBudgets
{
    public Dictionary<string, BudgetProcess> Countries { get; set; } = new(StringComparer.Ordinal);

    public BudgetProcess this[string country] => Countries[country];

    public BudgetProcess For(string country)
    {
        if (!Countries.TryGetValue(country, out var p))
            Countries[country] = p = new BudgetProcess();
        return p;
    }
}
