using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>
/// Бюджет за месяц: номинальный ВВП, доходы по статьям от своих баз, расходы по плану (в доле структурного ВВП)
/// плюс автоматические пособия по безработице, проценты и долг. MODEL.md, «Бюджет и долг».
/// </summary>
public sealed class BudgetSystem : EconomySystem
{
    public override string Name => "Бюджет";
    public override SimPhase Phase => SimPhase.Budget;

    protected override void Run(World world, WorldEconomy economy, EconomyBalance balance)
    {
        var data = world.Data;
        var industries = data.Industries.Industries;
        var revenues = data.Budget.Revenues;
        var spending = data.Budget.Spending;
        var b = balance.Budget;
        var reference = Reference(economy);
        var socialIndex = spending.Select((s, k) => (s, k)).FirstOrDefault(t => t.s.Id == "social").k;

        foreach (var country in world.Countries)
        {
            var e = economy[country.Id];
            var previousExchange = LastExchange(e);

            double nominal = 0, resources = 0;
            for (var i = 0; i < industries.Count; i++)
            {
                var value = e.Output[i] * IndustryPrice(industries[i], e, economy, reference);
                nominal += value;
                if (industries[i].Id is "oil_gas" or "coal_ores")
                    resources += value;
            }
            e.NominalGdp = nominal;
            e.ResourceGdp = resources;

            for (var k = 0; k < revenues.Count; k++)
                e.Revenue[k] = e.RevenueRate[k] * EconomyCalibration.RevenueBase(revenues[k], e, e.Gap);

            var structural = e.StructuralGdp;
            for (var k = 0; k < spending.Count; k++)
                e.Spending[k] = e.SpendingPlan[k] * structural;
            var stabilizer = EconomyBalance.ByIncome(b.Stabilizers, e.IncomeIndex) * (e.Unemployment - e.NaturalUnemployment) * structural;
            e.Spending[socialIndex] = Math.Max(0.5 * e.Spending[socialIndex], e.Spending[socialIndex] + stabilizer);

            e.MarketRate = NewBorrowingRate(e, economy, b, marketOnly: false);

            if (economy.Warmup)
            {
                // В разогреве долг и его стоимость держатся на стартовом уровне: уровни потом всё равно вернутся к данным.
                e.Debt = e.DebtRatio0 * e.NominalGdp;
                e.Interest = e.EffectiveRate * e.Debt;
                continue;
            }
            e.Interest = e.EffectiveRate * e.Debt;
            var deficit = (e.TotalSpending + e.Interest - e.TotalRevenue) / 12;
            var valuation = e.ForeignDebtShare * e.Debt * Math.Log(e.ExchangeRate / previousExchange);
            var before = e.Debt;
            e.Debt = Math.Max(0, e.Debt + deficit + valuation);
            e.EffectiveRate += (e.MarketRate - e.EffectiveRate) / b.DebtMaturityMonths;
            if (deficit > 0 && e.Debt > 0)
                e.EffectiveRate = (e.EffectiveRate * Math.Max(before, 0) + e.MarketRate * deficit) / (Math.Max(before, 0) + deficit);
        }
    }

    /// <summary>
    /// Ставка, под которую государство занимает сейчас. Рыночная часть: в своей валюте — от ставки ЦБ,
    /// в иностранной — от долларовой, плюс премия за срок и за риск по рейтингу. Льготная часть долга
    /// (кредиты развития, старые займы) остаётся на стартовой средней ставке и меняется пропорционально рынку.
    /// </summary>
    public static double NewBorrowingRate(CountryEconomy e, WorldEconomy economy, FiscalBalance b, bool marketOnly)
    {
        var referenceRate = economy.Currencies[Reference(economy).Currency].PolicyRate;
        var domestic = e.PolicyRate + b.TermPremium + e.Spread;
        var foreign = referenceRate + b.TermPremium + b.ForeignPremium + e.Spread;
        var market = (1 - e.ForeignDebtShare) * domestic + e.ForeignDebtShare * foreign;
        if (marketOnly)
            return market;
        // Льготная и старая часть долга дорожает и дешевеет в той же пропорции, что и рынок.
        var legacy = e.MarketRate0 > 0.005 ? e.EffectiveRate0 * market / e.MarketRate0 : e.EffectiveRate0 + market - e.MarketRate0;
        return Math.Clamp(e.MarketDebtShare * market + (1 - e.MarketDebtShare) * legacy, 0, b.RateCap);
    }

    /// <summary>
    /// Цена продукции отрасли в местных единицах: сырьё — по мировой цене в местной валюте,
    /// сельское хозяйство — наполовину по мировой, остальное — по внутренним ценам.
    /// </summary>
    public static double IndustryPrice(IndustryRecord industry, CountryEconomy e, WorldEconomy economy, CountryEconomy reference)
    {
        var worldPriceLocal = reference.Price * e.ExchangeRate;
        return industry.Id switch
        {
            "oil_gas" => worldPriceLocal * economy.OilPrice,
            "coal_ores" => worldPriceLocal * economy.OresPrice,
            "agriculture" => 0.5 * e.Price + 0.5 * worldPriceLocal * economy.FoodPrice,
            _ => e.Price,
        };
    }

    /// <summary>Курс прошлого месяца — последняя запись в истории.</summary>
    static double LastExchange(CountryEconomy e)
    {
        var last = (e.HistoryIndex + e.ExchangeHistory.Length - 1) % e.ExchangeHistory.Length;
        return e.ExchangeHistory[last];
    }
}
