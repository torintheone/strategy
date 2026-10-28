using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>
/// Бюджет на новый год (1 января), пока страной управляет «автопилот»: первичное сальдо постепенно
/// идёт к уровню, который стабилизирует долг и медленно возвращает его к ориентиру. Шаг — не больше
/// MaxStep ВВП в год, а при огромном разрыве (дефицит в десятки процентов ВВП) — до четверти разрыва. С этапа 3 бюджет игрока будет проходить через парламент. MODEL.md, «Бюджетное правило».
/// </summary>
public sealed class FiscalPlanSystem : EconomySystem
{
    public override string Name => "Бюджетный план";
    public override SimPhase Phase => SimPhase.Budget;
    public override SimFrequency Frequency => SimFrequency.Yearly;

    protected override void Run(World world, WorldEconomy economy, EconomyBalance balance)
    {
        var revenues = world.Data.Budget.Revenues;
        foreach (var country in world.Countries)
        {
            var e = economy[country.Id];
            if (MustDefault(e, economy, balance.Budget))
                Restructure(e, balance.Budget, world.Date.Year);
            Plan(e, revenues, balance.Budget);
        }
    }

    /// <summary>
    /// Долг стал неподъёмным: реальные проценты (за вычетом ожидаемой инфляции, которая сама обесценивает долг)
    /// съедают больше DefaultInterestShare доходов, или долг выше порога для группы дохода и заметно выше
    /// стартового. Эмитенты резервных валют по размеру долга не дефолтят.
    /// </summary>
    public static bool MustDefault(CountryEconomy e, WorldEconomy economy, FiscalBalance b)
    {
        var revenue = e.TotalRevenue;
        var realInterest = Math.Max(0, e.EffectiveRate - Math.Max(0, e.Expectations)) * e.Debt;
        if (revenue > 0 && realInterest / revenue > b.DefaultInterestShare)
            return true;
        var reserve = economy.Currencies.TryGetValue(e.Currency, out var cur) && cur.Reserve;
        var limit = Math.Max(EconomyBalance.ByIncome(b.DefaultDebt, e.IncomeIndex), b.DefaultDebtGrowth * e.DebtRatio0);
        return !reserve && e.DebtRatio > limit;
    }

    /// <summary>
    /// Дефолт и реструктуризация: кредиторы списывают долю долга, ставка по оставшемуся долгу возвращается
    /// к стартовой, экономика проваливается в спад, рейтинг на несколько лет падает почти до дна.
    /// </summary>
    public static void Restructure(CountryEconomy e, FiscalBalance b, int year)
    {
        e.Debt *= 1 - b.Haircut;
        e.EffectiveRate = Math.Min(e.EffectiveRate, e.EffectiveRate0);
        e.EffectiveRate0 = e.EffectiveRate;
        e.MarketRate0 = e.MarketRate;
        e.GapCore -= b.DefaultRecession;
        e.Defaults++;
        e.LastDefaultYear = year;
    }

    public static void Plan(CountryEconomy e, IReadOnlyList<RevenueRecord> revenues, FiscalBalance b)
    {
        var structural = e.StructuralGdp;
        if (structural <= 0)
            return;
        var revenue = 0.0;
        for (var k = 0; k < revenues.Count; k++)
            revenue += e.Revenue[k] / (1 + (revenues[k].Elasticity - 1) * (revenues[k].Base is "resources" or "imports" ? 0 : e.Gap));
        var revenueRatio = revenue / structural;
        var spendingRatio = e.SpendingPlan.Sum();
        var current = revenueRatio - spendingRatio;

        var nominalGrowth = (1 + e.PotentialGrowth) * (1 + e.Expectations) - 1;
        var debt = e.Debt / structural;
        var stabilizing = (e.EffectiveRate - nominalGrowth) / (1 + nominalGrowth) * debt;
        var target = Math.Clamp(stabilizing + b.DebtCorrection * (debt - e.DebtAnchor), b.TargetMin, b.TargetMax);
        // Обычно сальдо меняется не больше чем на MaxStep ВВП в год; при огромном разрыве — на четверть разрыва.
        var gap = target - current;
        var limit = Math.Max(b.MaxStep, b.LargeGapShare * Math.Abs(gap));
        var step = Math.Clamp(b.ConsolidationSpeed * gap, -limit, limit);
        e.PrimaryBalancePlan = current + step;

        var newSpending = Math.Max(b.SpendingFloor, revenueRatio - e.PrimaryBalancePlan);
        var scale = spendingRatio > 0 ? newSpending / spendingRatio : 1;
        for (var k = 0; k < e.SpendingPlan.Length; k++)
            e.SpendingPlan[k] *= scale;
    }
}
