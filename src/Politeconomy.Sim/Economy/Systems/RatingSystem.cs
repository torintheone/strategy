using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>
/// Кредитный рейтинг (0 — дефолт, 21 — AAA) по формуле: богатство и размер экономики повышают его,
/// долг выше 60% ВВП, дефицит выше 3% и высокая инфляция снижают, резервная валюта добавляет.
/// От рейтинга зависит премия за риск в ставке по долгу. MODEL.md, «Рейтинг и ставки по долгу».
/// </summary>
public sealed class RatingSystem : EconomySystem
{
    public override string Name => "Кредитный рейтинг";
    public override SimPhase Phase => SimPhase.Budget;

    protected override void Run(World world, WorldEconomy economy, EconomyBalance balance)
    {
        foreach (var country in world.Countries)
            Assess(balance, economy, economy[country.Id], LawModel.Effect(world, country.Id, "spread") / 100);
    }

    /// <summary>Рейтинг и премия за риск. ExtraSpread — надбавка от законов (валютный контроль), доля.</summary>
    public static void Assess(EconomyBalance balance, WorldEconomy economy, CountryEconomy e, double extraSpread = 0)
    {
        var b = balance.Rating;
        var ppp = e.Ppp0 * (e.RealGdp / e.RealGdp0) / (e.Population / e.Population0);
        var currency = economy.Currencies.GetValueOrDefault(e.Currency);
        var inflation = e.Price / e.YearAgo(e.PriceHistory) - 1;
        var score = b.Base
                    + Math.Max(b.WealthFloor, b.Wealth * Math.Log(Math.Max(ppp, 300) / b.WealthReference))
                    + Math.Max(b.SizeFloor, b.Size * Math.Log(Math.Max(e.GdpUsd, 1e6) / 1e12))
                    - b.Debt * Math.Max(0, e.DebtRatio - b.DebtThreshold)
                    - b.Deficit * Math.Max(0, -e.BalanceRatio - b.DeficitThreshold)
                    - b.Inflation * Math.Log(1 + Math.Max(0, inflation - b.InflationThreshold) / 0.1);
        if (e.BudgetFailedYear > 0 && e.BudgetFailedYear == (int)CurrentYear(economy))
            score -= b.BudgetFailure;
        if (currency is { Reserve: true })
            score += b.ReserveCurrency + (currency.Id == "USD" ? b.DollarBonus : 0);
        if (e.Defaults > 0 && economy.Months > 0)
        {
            // После дефолта рейтинг лежит у дна и за DefaultRatingYears лет постепенно освобождается.
            var years = e.LastDefaultYear > 0 ? Math.Max(0, CurrentYear(economy) - e.LastDefaultYear) : 99;
            var cap = 1 + 20 * Math.Min(1, years / balance.Budget.DefaultRatingYears);
            score = Math.Min(score, cap);
        }
        e.Rating = Math.Clamp(score, 0, 21);
        e.Spread = Math.Min(b.SpreadMax, b.SpreadBase * Math.Exp(b.SpreadSlope * (21 - e.Rating))) + extraSpread;
    }

    /// <summary>Год по календарю модели (старт — 2026, во время разогрева календарь стоит).</summary>
    static double CurrentYear(WorldEconomy economy) => economy.StartYear + economy.Months / 12.0;

    static readonly string[] Letters =
        ["D", "C", "CC", "CCC−", "CCC", "CCC+", "B−", "B", "B+", "BB−", "BB", "BB+", "BBB−", "BBB", "BBB+", "A−", "A", "A+", "AA−", "AA", "AA+", "AAA"];

    /// <summary>Буквенный рейтинг по шкале 0–21.</summary>
    public static string Letter(double score) => Letters[(int)Math.Clamp(Math.Round(score), 0, 21)];
}
