using Politeconomy.Sim.Data;
using Politeconomy.Sim.Economy;
using Politeconomy.Sim.Economy.Systems;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Society;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Budget;

/// <summary>
/// Бюджетный цикл страны игрока: в сентябре минфин вносит проект на следующий год (по правилу автопилота), игрок
/// может его изменить, в октябре парламент голосует; при провале — повторные голосования до декабря, а если бюджет
/// так и не принят, с 1 января действует прошлогодний и рейтинг понижается. В течение года можно вносить поправки.
/// MODEL.md, «Бюджетный цикл».
/// </summary>
public static class BudgetCycle
{
    /// <summary>Бюджет страны на этот год решался через парламент — автопилоту его трогать нельзя.</summary>
    public static bool HandledByPlayer(World world, string country) =>
        world.PlayerCountry == country && world.Budgets?.Countries.TryGetValue(country, out var p) == true
        && p.Year == world.Date.Year && p.Stage != "none";

    public static void Step(World world)
    {
        if (world.PlayerCountry is not { } id || world.Economy is not { } economy || world.Budgets is not { } budgets)
            return;
        var b = world.Data.BudgetCycleBalance;
        var p = budgets.For(id);
        var e = economy[id];
        var date = world.Date;

        if (date.Month == 1 && p.Year == date.Year)
            Apply(world, id, p, e);
        if (date.Month == b.DraftMonth && p.Year != date.Year + 1)
            Draft(world, id, p, e, b);
        if (p.HasDraft && p.NextVote <= date)
            VoteDraft(world, id, p, b);
        if (p.AmendVote is { } when && when <= date)
            VoteAmendment(world, id, p, e);
    }

    static void Apply(World world, string id, BudgetProcess p, CountryEconomy e)
    {
        if (p.Stage == "passed")
        {
            e.SpendingPlan = p.Spending.ToArray();
            e.TaxPolicy = p.Taxes.ToArray();
            Log(world, id, p, $"Бюджет на {p.Year} год вступил в силу.");
        }
        else
        {
            e.BudgetFailedYear = p.Year;
            Log(world, id, p, $"Бюджет на {p.Year} год так и не принят: страна живёт по прошлогоднему бюджету, кредитный рейтинг понижен.");
        }
        p.Year = 0;
        p.Stage = "none";
        p.NextVote = null;
        p.Votes.Clear();
        p.Forecast = null;
        p.Version++;
    }

    static void Draft(World world, string id, BudgetProcess p, CountryEconomy e, BudgetCycleBalance b)
    {
        var data = world.Data;
        var proposal = FiscalPlanSystem.Propose(e, data.Budget.Revenues, data.Economy.Budget, BudgetSystem.LawSpending(world, id, data.Budget.Spending));
        p.Year = world.Date.Year + 1;
        p.Stage = "draft";
        p.Spending = proposal?.Spending ?? e.SpendingPlan.ToArray();
        p.Taxes = e.TaxPolicy.ToArray();
        p.NextVote = new DateOnly(world.Date.Year, b.VoteMonth, 1);
        p.Votes.Clear();
        p.Forecast = Forecast(world, id, p.Spending, p.Taxes);
        Log(world, id, p, $"Министерство финансов внесло проект бюджета на {p.Year} год. Парламент проголосует {Fmt.Date(p.NextVote.Value)}; " +
                          "до этого проект можно изменить на экране бюджета.");
    }

    static void VoteDraft(World world, string id, BudgetProcess p, BudgetCycleBalance b)
    {
        var forecast = p.Forecast = Forecast(world, id, p.Spending, p.Taxes);
        var parliament = world.Government![id].Parliament;
        var vote = Cast(world, id, parliament, forecast);
        p.Votes.Add(vote);
        if (vote.Passed)
        {
            p.Stage = "passed";
            p.NextVote = null;
            Log(world, id, p, vote.Decree
                ? $"Бюджет на {p.Year} год утверждён указом."
                : $"{parliament.Name} принял бюджет на {p.Year} год: за {vote.Yes}, против {vote.No}.");
        }
        else if (world.Date.Month < b.LastVoteMonth)
        {
            p.Stage = "rejected";
            p.NextVote = new DateOnly(world.Date.Year, world.Date.Month, 1).AddMonths(1);
            Log(world, id, p, $"{parliament.Name} отклонил проект бюджета на {p.Year} год: за {vote.Yes}, против {vote.No}, нужно {vote.Required}. " +
                              $"Повторное голосование {Fmt.Date(p.NextVote.Value)} — измените проект, чтобы найти поддержку.");
        }
        else
        {
            p.Stage = "failed";
            p.NextVote = null;
            Log(world, id, p, $"{parliament.Name} снова отклонил бюджет на {p.Year} год. С 1 января страна будет жить по прошлогоднему бюджету.");
        }
    }

    static void VoteAmendment(World world, string id, BudgetProcess p, CountryEconomy e)
    {
        var forecast = p.Forecast = Forecast(world, id, p.AmendSpending!, p.AmendTaxes!);
        var parliament = world.Government![id].Parliament;
        var vote = Cast(world, id, parliament, forecast);
        if (vote.Passed)
        {
            e.SpendingPlan = p.AmendSpending!.ToArray();
            e.TaxPolicy = p.AmendTaxes!.ToArray();
            Log(world, id, p, vote.Decree ? "Поправки к бюджету введены указом." : $"{parliament.Name} принял поправки к бюджету: за {vote.Yes}, против {vote.No}.");
        }
        else
            Log(world, id, p, $"{parliament.Name} отклонил поправки к бюджету: за {vote.Yes}, против {vote.No}, нужно {vote.Required}.");
        p.AmendSpending = p.AmendTaxes = null;
        p.AmendVote = null;
        p.Forecast = null;
    }

    /// <summary>Голосование о бюджете: каждая фракция смотрит на своих избирателей.</summary>
    static VoteResult Cast(World world, string id, Parliament parliament, BudgetForecast forecast)
    {
        var scale = world.Data.BudgetCycleBalance.SupportScale;
        return ParliamentVote.Cast(world, parliament, party => forecast.PartySupport(PoliticsModel.Voters(world, id, party), scale));
    }

    static void Log(World world, string id, BudgetProcess p, string text)
    {
        p.LastResult = text;
        p.Version++;
        world.Log(id, "budget", text);
    }

    /// <summary>
    /// Прогноз бюджета с расходами spending (доли ВВП по статьям) и множителями налогов taxes: сальдо, изменение
    /// дохода каждого класса, их отношение, одобрение в обществе и ожидаемое голосование. Считается по нынешнему месяцу.
    /// </summary>
    public static BudgetForecast Forecast(World world, string id, double[] spending, double[] taxes)
    {
        var data = world.Data;
        var b = data.BudgetCycleBalance;
        var economy = world.Economy!;
        var e = economy[id];
        var gdp = Math.Max(1e-9, e.NominalGdp);
        var structural = e.StructuralGdp;
        var revenue = e.Revenue.Select((r, k) => r * (k < taxes.Length ? taxes[k] : 1) / Math.Max(1e-9, BudgetSystem.Policy(e, k))).ToArray();
        var outlays = e.Spending.Select((s, k) => Math.Max(0, s + ((k < spending.Length ? spending[k] : e.SpendingPlan[k]) - e.SpendingPlan[k]) * structural)).ToArray();

        var classes = data.Classes.Classes;
        var n = classes.Count;
        var change = new double[n];
        var attitudes = new double[n];
        if (world.Society?.Countries.GetValueOrDefault(id) is { } society)
        {
            var composition = SocietyModel.Compose(world, world[id]);
            double Effect(string key) => LawModel.Effect(world, id, key);
            var before = ClassIncome.Compute(data, classes, e, economy, composition, Effect).Disposable;
            var (savedRevenue, savedSpending) = (e.Revenue, e.Spending);
            (e.Revenue, e.Spending) = (revenue, outlays);
            var after = ClassIncome.Compute(data, classes, e, economy, composition, Effect).Disposable;
            (e.Revenue, e.Spending) = (savedRevenue, savedSpending);
            var items = data.Budget.Spending;
            for (var k = 0; k < n; k++)
            {
                change[k] = before[k] > 0 ? after[k] / before[k] - 1 : 0;
                var attitude = b.IncomeAttitude * change[k] * 100;
                for (var i = 0; i < items.Count && i < spending.Length; i++)
                    if (items[i].Supporters?.Contains(classes[k].Id) == true && e.SpendingPlan[i] > 1e-6)
                        attitude += b.SpendingAttitude * (spending[i] / e.SpendingPlan[i] - 1);
                attitudes[k] = Math.Clamp(attitude, -b.AttitudeCap, b.AttitudeCap);
            }
        }
        var influence = world.Society?.Countries.GetValueOrDefault(id)?.Influence ?? new double[n];
        var weighted = 0.0;
        for (var k = 0; k < n; k++)
            weighted += influence[k] / 100 * attitudes[k];
        var balance = (revenue.Sum() - outlays.Sum() - e.Interest) / gdp;
        // Парламент не любит, когда дефицит растёт больше чем на DeficitTolerance ВВП по сравнению с нынешним.
        var growth = e.BalanceRatio - balance;
        var penalty = b.DeficitPenalty * Math.Max(0, growth - b.DeficitTolerance);
        var support = Math.Clamp(0.5 + 0.5 * Math.Tanh(weighted / b.SupportScale) - penalty, 0, 1);
        var forecast = new BudgetForecast(revenue.Sum() / gdp, outlays.Sum() / gdp, e.Interest / gdp, balance, change, attitudes, support,
            new VoteResult(0, 0, 0, 0, 0, 0, true, true), penalty);
        var parliament = world.Government?.Countries.GetValueOrDefault(id)?.Parliament;
        if (parliament == null)
            return forecast;
        var factions = parliament.Factions
            .Select(f => new FactionSupport(f.Party, f.Seats, f.Ruling, forecast.PartySupport(PoliticsModel.Voters(world, id, f.Party), b.SupportScale)))
            .ToList();
        return forecast with
        {
            Vote = ParliamentVote.Forecast(data.GovernmentBalance.Parliament, parliament.Kind, parliament.Seats, parliament.Discipline, factions,
                constitutional: false),
        };
    }
}
