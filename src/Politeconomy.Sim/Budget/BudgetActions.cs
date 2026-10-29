using Politeconomy.Sim.Economy.Systems;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Budget;

/// <summary>Решения игрока о бюджете. Вызываются в потоке симуляции (через SimulationHost.Post).</summary>
public static class BudgetActions
{
    /// <summary>
    /// Изменить бюджет: если есть проект на следующий год — меняется он, иначе это черновик поправок к текущему.
    /// Значения ограничиваются пределами ползунков; прогноз пересчитывается.
    /// </summary>
    public static bool Edit(World world, string countryId, double[] spending, double[] taxes)
    {
        if (world.PlayerCountry != countryId || world.Economy is not { } economy || world.Budgets is not { } budgets)
            return false;
        var b = world.Data.BudgetCycleBalance;
        var e = economy[countryId];
        var p = budgets.For(countryId);
        var baseSpending = p.HasDraft ? p.Spending : e.SpendingPlan;
        var s = spending.Select((x, k) => Math.Clamp(x, 0, Math.Max(b.SpendingMax * (k < baseSpending.Length ? baseSpending[k] : 0), 0.005))).ToArray();
        var t = taxes.Select(x => Math.Clamp(x, b.TaxMin, b.TaxMax)).ToArray();
        if (p.HasDraft)
            (p.Spending, p.Taxes) = (s, t);
        else
            (p.AmendSpending, p.AmendTaxes) = (s, t);
        p.Forecast = BudgetCycle.Forecast(world, countryId, s, t);
        p.Version++;
        return true;
    }

    /// <summary>Внести поправки к текущему бюджету в парламент: голосование в первый день следующего месяца.</summary>
    public static bool SubmitAmendment(World world, string countryId)
    {
        var p = world.Budgets?.Countries.GetValueOrDefault(countryId);
        if (world.PlayerCountry != countryId || p?.AmendSpending == null || p.AmendVote != null)
            return false;
        p.AmendVote = new DateOnly(world.Date.Year, world.Date.Month, 1).AddMonths(1);
        p.Version++;
        world.Log(countryId, "budget", $"Поправки к бюджету внесены в парламент. Голосование {Fmt.Date(p.AmendVote.Value)}.");
        return true;
    }

    /// <summary>Отказаться от черновика поправок (если они ещё не внесены).</summary>
    public static void DiscardAmendment(World world, string countryId)
    {
        var p = world.Budgets?.Countries.GetValueOrDefault(countryId);
        if (p == null || p.AmendVote != null)
            return;
        p.AmendSpending = p.AmendTaxes = null;
        p.Forecast = null;
        p.Version++;
    }

    /// <summary>Можно ли игроку ставить ключевую ставку: центробанк подчинён правительству, валюта своя и плавающая.</summary>
    public static bool CanSetRate(World world, string countryId)
    {
        if (world.Economy is not { } economy || !economy.Countries.TryGetValue(countryId, out var e))
            return false;
        var cur = economy.Currencies.GetValueOrDefault(e.Currency);
        return cur is { IsFloating: true } && cur.Home == countryId && LawModel.CentralBankObeys(world, countryId);
    }

    /// <summary>Задать ключевую ставку (доля, 0,1 = 10%) или вернуть решение центробанку (null).</summary>
    public static bool SetRate(World world, string countryId, double? rate)
    {
        if (world.PlayerCountry != countryId || !CanSetRate(world, countryId))
            return false;
        var cur = world.Economy!.Currencies[world.Economy[countryId].Currency];
        var b = world.Data.Economy.Monetary;
        cur.ManualRate = rate is { } r ? Math.Clamp(r, b.Floor, b.Ceiling) : null;
        world.Log(countryId, "rate", rate is { } x
            ? $"Правительство поручило центробанку держать ключевую ставку {Fmt.Value("percent", x * 100)}."
            : "Ключевую ставку снова определяет центробанк по своим правилам.");
        return true;
    }
}
