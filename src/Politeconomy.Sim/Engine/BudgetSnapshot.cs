using Politeconomy.Sim.Budget;
using Politeconomy.Sim.Economy.Systems;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Engine;

/// <summary>
/// Бюджет страны игрока в снимке. Доли — от ВВП. SpendingPlan и TaxPolicy — действующий бюджет; Edit* — то, что
/// сейчас редактируется: проект на следующий год (DraftYear > 0), черновик поправок или (если ничего нет) действующий.
/// RevenueShare и SpendingShare — фактические доходы и расходы по статьям в этом месяце.
/// </summary>
public sealed record BudgetSnapshot(
    string Country,
    int DraftYear,
    string Stage,
    DateOnly? NextVote,
    IReadOnlyList<VoteResult> Votes,
    bool Amending,
    DateOnly? AmendVote,
    double[] SpendingPlan,
    double[] TaxPolicy,
    double[] EditSpending,
    double[] EditTaxes,
    double[] RevenueShare,
    double[] SpendingShare,
    double Interest,
    double Balance,
    BudgetForecast? Forecast,
    string? LastResult,
    bool CanSetRate,
    double PolicyRate,
    double? ManualRate,
    int Version)
{
    public static BudgetSnapshot? Capture(World world)
    {
        if (world.PlayerCountry is not { } id || world.Economy?.Countries.GetValueOrDefault(id) is not { } e)
            return null;
        var p = world.Budgets?.Countries.GetValueOrDefault(id);
        var gdp = Math.Max(1e-9, e.NominalGdp);
        var policy = Enumerable.Range(0, e.Revenue.Length).Select(k => BudgetSystem.Policy(e, k)).ToArray();
        var editSpending = p is { HasDraft: true } ? p.Spending : p?.AmendSpending ?? e.SpendingPlan;
        var editTaxes = p is { HasDraft: true } ? p.Taxes : p?.AmendTaxes ?? policy;
        var cur = world.Economy.Currencies.GetValueOrDefault(e.Currency);
        return new BudgetSnapshot(
            id,
            p?.Year ?? 0,
            p?.Stage ?? "none",
            p?.NextVote,
            p?.Votes.ToList() ?? [],
            p?.AmendSpending != null,
            p?.AmendVote,
            e.SpendingPlan.ToArray(),
            policy,
            editSpending.ToArray(),
            editTaxes.ToArray(),
            e.Revenue.Select(r => r / gdp).ToArray(),
            e.Spending.Select(s => s / gdp).ToArray(),
            e.Interest / gdp,
            e.BalanceRatio,
            p?.Forecast,
            p?.LastResult,
            BudgetActions.CanSetRate(world, id),
            e.PolicyRate,
            cur?.ManualRate,
            p?.Version ?? 0);
    }
}
