using Politeconomy.Sim.Data;
using Politeconomy.Sim.Economy.Systems;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy;

/// <summary>
/// Стартовое состояние экономики из данных: отрасли, капитал, производительность, бюджет, ставки и валюты.
/// Модель на старте воспроизводит данные: ВВП, его структуру, долг, сальдо, инфляцию и безработицу.
/// </summary>
public static class EconomyCalibration
{
    /// <summary>Валюты, долг в которых не несёт валютного риска для эмитента.</summary>
    static readonly HashSet<string> HardCurrencies = ["USD", "EUR", "JPY", "GBP", "CHF"];

    public static WorldEconomy Calibrate(World world)
    {
        var data = world.Data;
        var economy = new WorldEconomy { StartYear = world.Date.Year };
        foreach (var country in world.Countries)
            economy.Countries[country.Id] = CalibrateCountry(data, country);

        var totalUsd = economy.Countries.Values.Sum(e => e.GdpUsd);
        foreach (var e in economy.Countries.Values)
            e.WorldShare = e.GdpUsd / totalUsd;
        economy.FrontierPpp = economy.Countries.TryGetValue("USA", out var usa) ? usa.Ppp0 : economy.Countries.Values.Max(e => e.Ppp0);
        economy.WorldInflation = economy.Countries.Values.Sum(e => e.WorldShare * e.Inflation);

        CalibrateCurrencies(world, economy);
        foreach (var country in world.Countries)
        {
            var e = economy[country.Id];
            RatingSystem.Assess(data.Economy, economy, e);
            e.Spread0 = e.Spread;
            e.MarketRate0 = BudgetSystem.NewBorrowingRate(e, economy, data.Economy.Budget, marketOnly: true);
            e.MarketRate = e.EffectiveRate0;
        }
        return economy;
    }

    static CountryEconomy CalibrateCountry(GameData data, Country country)
    {
        var b = data.Economy;
        // Калибровка всегда идёт по исходным данным, а не по текущим показателям модели.
        double S(string id, double fallback = 0) => country.Info.Stats.TryGetValue(id, out var v) ? v.Value : fallback;
        var industries = data.Industries.Industries;
        var n = industries.Count;
        var income = (int)country.Income;
        var incomeKey = WorldFactory.IncomeId(country.Income);

        var y0 = S("gdp_usd");
        var labor = S("labor_force");
        var u0 = Math.Clamp(S("unemployment_pct") / 100, 0.005, 0.8);
        var uRaw = S("unemployment_natural_pct", u0 * 100) / 100;
        var uN = Math.Clamp(Math.Clamp(uRaw, u0 - b.Labor.NaturalMaxShift, u0 + b.Labor.NaturalMaxShift),
            b.Labor.NaturalMin, Math.Max(b.Labor.NaturalMax, u0));
        var trend = Math.Clamp(S("gdp_growth_trend_pct", 3) / 100, b.Supply.TrendMin, b.Supply.TrendMax);
        var first = Math.Clamp(S("gdp_growth_forecast_pct", trend * 100) / 100, trend - b.Supply.FirstYearBand, trend + b.Supply.FirstYearBand);
        var inflation = Math.Clamp(S("inflation_pct") / 100, b.Prices.InflationMin, b.Prices.InflationMax);
        var target = Math.Clamp(S("inflation_anchor_pct", 3) / 100, b.Prices.AnchorMin, b.Prices.AnchorMax);
        var investment = Math.Clamp(S("investment_pct_gdp", 22) / 100, 0.08, 0.5);
        var exports = Math.Clamp(S("exports_pct_gdp", 30) / 100, 0.01, 2.5);
        var imports = Math.Clamp(S("imports_pct_gdp", 35) / 100, 0.01, 2.5);
        var oilRent = (S("oil_rents_pct_gdp") + S("gas_rents_pct_gdp")) / 100;
        var oresRent = (S("coal_rents_pct_gdp") + S("mineral_rents_pct_gdp")) / 100;

        var e = new CountryEconomy
        {
            Currency = country.Info.Currency,
            IncomeIndex = income,
            RealGdp0 = y0,
            Population0 = S("population"),
            Population = S("population"),
            LaborForce = labor,
            Ppp0 = S("gdp_ppp_per_capita", S("gdp_per_capita_usd") * 2),
            TrendGrowth = trend,
            FirstYearGrowth = first,
            PotentialGrowth = first,
            PopulationGrowth0 = Math.Clamp(S("population_growth_pct", 1) / 100, -0.03, 0.05),
            NaturalUnemployment = u0,
            NaturalUnemploymentStart = u0,
            NaturalUnemploymentLong = uN,
            Unemployment = u0,
            InflationTarget = inflation,
            InflationStart = inflation,
            InflationLong = target,
            Inflation = inflation,
            Expectations = inflation,
            Credibility = EconomyBalance.ByIncome(b.Prices.Credibility, income),
            FoodWeight = EconomyBalance.ByIncome(b.Prices.FoodWeight, income),
            InvestmentRate0 = investment,
            InvestmentRate = investment,
            Exports0 = exports,
            Exports = exports,
            Imports0 = imports,
            Imports = imports,
            CurrentAccount0 = Math.Clamp(S("current_account_pct_gdp") / 100, -b.Demand.CurrentAccountLimit, b.Demand.CurrentAccountLimit),
            Openness = Math.Clamp((exports + imports) / 2, 0, 1),
            OilNet = oilRent - b.Demand.OilConsumption,
            OresNet = oresRent - b.Demand.OresConsumption,
            CommodityExports = Math.Clamp(b.Demand.CommodityExports * (oilRent + oresRent) / exports, 0, 0.9),
            OilImports = Math.Clamp(Math.Max(0, b.Demand.OilConsumption - oilRent) / imports, 0, 0.4),
            RealGdp = y0,
            NominalGdp = y0,
        };
        e.CurrentAccount = e.CurrentAccount0;

        // Отрасли: доли выпуска и занятости, капитал из нормы инвестиций, производительность — остаток.
        var shares = IndustryStructure.Compute(industries, id => S(id), incomeKey);
        var va = shares.Output.Select(s => s * y0).ToArray();
        var employment = shares.Employment;
        for (var i = 0; i < n; i++)
            if (va[i] > 0 && employment[i] <= 0)
                employment[i] = 1e-6;
        var growth = Math.Max(trend, 0);
        var userCost = SolveUserCost(industries, va, growth, investment * y0, b.Supply);
        var capital = new double[n];
        for (var i = 0; i < n; i++)
            capital[i] = industries[i].Alpha * va[i] / (userCost + industries[i].Depreciation);
        var required = Enumerable.Range(0, n).Sum(i => (growth + industries[i].Depreciation) * capital[i]);
        var potentialLabor = labor * (1 - u0);
        e.EmploymentShare = employment;
        e.InvestmentShare = Enumerable.Range(0, n).Select(i => required > 0 ? (growth + industries[i].Depreciation) * capital[i] / required : 0).ToArray();
        e.Capital = capital;
        e.Tfp = Enumerable.Range(0, n).Select(i =>
        {
            if (va[i] <= 0)
                return 0;
            var a = industries[i].Alpha;
            return va[i] / (Math.Pow(capital[i], a) * Math.Pow(potentialLabor * employment[i], 1 - a));
        }).ToArray();
        e.BaseOutput = va;
        e.Potential = (double[])va.Clone();
        e.Output = (double[])va.Clone();
        e.ResourceGdp = ResourceOutput(industries, va);

        CalibrateBudget(data, country, e, S);

        // История за 12 месяцев до старта — чтобы рост и инфляция «за год» сразу совпадали с данными.
        // Месяц старта дописывается в историю после первых показателей (EconomyModel.Start).
        var growth0 = Math.Clamp(S("gdp_growth_pct") / 100, -0.5, 0.5);
        for (var j = 0; j < 12; j++)
        {
            var monthsBack = 12 - j;
            e.RealGdpHistory[j] = y0 / Math.Pow(1 + growth0, monthsBack / 12.0);
            e.PriceHistory[j] = 1 / Math.Pow(1 + inflation, monthsBack / 12.0);
        }
        return e;
    }

    /// <summary>Доходность капитала, при которой нужные для роста инвестиции совпадают с фактической нормой инвестиций.</summary>
    static double SolveUserCost(IReadOnlyList<IndustryRecord> industries, double[] va, double growth, double investment, SupplyBalance b)
    {
        double Need(double r) => industries.Select((ind, i) => (growth + ind.Depreciation) * ind.Alpha * va[i] / (r + ind.Depreciation)).Sum();
        double lo = b.UserCostMin, hi = b.UserCostMax;
        if (Need(lo) <= investment)
            return lo;
        if (Need(hi) >= investment)
            return hi;
        for (var k = 0; k < 60; k++)
        {
            var mid = (lo + hi) / 2;
            if (Need(mid) > investment)
                lo = mid;
            else
                hi = mid;
        }
        return (lo + hi) / 2;
    }

    public static double ResourceOutput(IReadOnlyList<IndustryRecord> industries, double[] values)
    {
        var sum = 0.0;
        for (var i = 0; i < industries.Count; i++)
            if (industries[i].Id is "oil_gas" or "coal_ores")
                sum += values[i];
        return sum;
    }

    static void CalibrateBudget(GameData data, Country country, CountryEconomy e, Func<string, double, double> s)
    {
        var b = data.Economy.Budget;
        var income = e.IncomeIndex;
        var incomeKey = WorldFactory.IncomeId(country.Income);
        var y0 = e.NominalGdp;

        var revenue = Math.Clamp(s("gov_revenue_pct_gdp", 25) / 100, 0.03, 0.8);
        var balance = Math.Clamp(s("budget_balance_pct_gdp", -3) / 100, -0.6, 0.6);
        var debt = Math.Clamp(s("gov_debt_pct_gdp", 50) / 100, 0, 3.5);
        var interest = Math.Clamp(s("interest_pct_gdp", 1.5) / 100, 0, Math.Min(0.15, b.InterestRevenueCap * revenue));
        var rate = debt > 0.01 ? Math.Clamp(interest / debt, 0.002, 0.25) : 0.03;
        interest = rate * debt;
        var nonInterest = Math.Max(b.SpendingFloor, revenue - balance - interest);

        // Доходы: природная рента — по ренте добычи, остальное — по типичной структуре группы дохода.
        var revenues = data.Budget.Revenues;
        var resourceGdp = e.ResourceGdp / y0;
        var resourceTax = Math.Min(b.ResourceTake * resourceGdp / 1.2, b.ResourceMaxShare * revenue);
        var otherShares = revenues.Select(r => r.Base == "resources" ? 0 : r.Shares.GetValueOrDefault(incomeKey)).ToArray();
        var otherSum = otherShares.Sum();
        e.RevenueRate = new double[revenues.Count];
        e.TaxPolicy = Enumerable.Repeat(1.0, revenues.Count).ToArray();
        e.Revenue = new double[revenues.Count];
        for (var k = 0; k < revenues.Count; k++)
        {
            var amount = revenues[k].Base == "resources" ? resourceTax : (revenue - resourceTax) * otherShares[k] / otherSum;
            var baseValue = RevenueBase(revenues[k], e, gap: 0) / y0;
            e.RevenueRate[k] = baseValue > 0 ? amount / baseValue : 0;
            e.Revenue[k] = e.RevenueRate[k] * baseValue * y0;
        }

        // Расходы: оборона — по военным расходам, остальное — по типичной структуре.
        var spending = data.Budget.Spending;
        var defense = Math.Min(s("military_pct_gdp", 1.5) / 100, 0.5 * nonInterest);
        var spendShares = spending.Select(x => x.Id == "defense" ? 0 : x.Shares.GetValueOrDefault(incomeKey)).ToArray();
        var spendSum = spendShares.Sum();
        e.SpendingPlan = spending.Select((x, k) => x.Id == "defense" ? defense : (nonInterest - defense) * spendShares[k] / spendSum).ToArray();
        e.Spending = e.SpendingPlan.Select(r => r * y0).ToArray();

        e.Debt = debt * y0;
        e.DebtRatio0 = debt;
        e.EffectiveRate = rate;
        e.EffectiveRate0 = rate;
        e.MarketRate = rate;
        e.Interest = interest * y0;
        e.PrimaryBalancePlan = revenue - nonInterest;
        e.PrimaryDeficitSmooth = nonInterest - revenue;
        e.ForeignDebtShare = HardCurrencies.Contains(e.Currency) ? 0 : EconomyBalance.ByIncome(b.ForeignDebtShare, income);
        e.DebtAnchor = EconomyBalance.ByIncome(b.DebtAnchor, income);
        e.MarketDebtShare = EconomyBalance.ByIncome(b.MarketDebtShare, income);
    }

    /// <summary>База дохода бюджета в местных единицах (годовая): зарплаты, прибыль, потребление, импорт, добыча.</summary>
    public static double RevenueBase(RevenueRecord revenue, CountryEconomy e, double gap) => revenue.Base switch
    {
        "resources" => e.ResourceGdp,
        "imports" => e.NominalGdp * e.Imports,
        _ => (e.NominalGdp - e.ResourceGdp) * (1 + (revenue.Elasticity - 1) * gap),
    };

    static void CalibrateCurrencies(World world, WorldEconomy economy)
    {
        var data = world.Data;
        var b = data.Economy;
        var records = data.Currencies.Currencies.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var groups = economy.Countries
            .GroupBy(kv => kv.Value.Currency, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var record = records.GetValueOrDefault(group.Key);
            var members = group.Select(kv => kv.Key).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var home = members.OrderByDescending(id => economy[id].GdpUsd).ThenBy(id => id, StringComparer.Ordinal).First();
            var weights = Weights(economy, members);
            var cur = new CurrencyState
            {
                Id = group.Key,
                Regime = PegHolds(record, economy, members, b.Monetary) ? record!.Regime : "float",
                Anchor = record?.Anchor,
                Reserve = record?.Reserve ?? false,
                Members = members,
                Home = home,
                Target = members.Sum(id => weights[id] * economy[id].InflationTarget),
                Neutral = members.Sum(id => weights[id] * EconomyBalance.ByIncome(b.Monetary.NeutralRate, economy[id].IncomeIndex)),
                Inflation = members.Sum(id => weights[id] * economy[id].Inflation),
                Expectations = members.Sum(id => weights[id] * economy[id].Expectations),
            };
            economy.Currencies[cur.Id] = cur;
        }

        // Сначала плавающие валюты: ставка из данных BIS или по правилу Тейлора, затем привязанные к ним.
        foreach (var cur in economy.Currencies.Values.Where(c => c.IsFloating))
        {
            var observed = ObservedRate(world, cur.Home);
            var taylor = cur.Neutral + cur.Inflation + b.Monetary.TaylorInflation * (cur.Inflation - cur.Target);
            if (observed is { } rate)
            {
                var implied = rate - cur.Inflation - b.Monetary.TaylorInflation * (cur.Inflation - cur.Target);
                cur.Neutral = Math.Clamp(implied, cur.Neutral - b.Monetary.NeutralBelow, cur.Neutral + b.Monetary.NeutralAbove);
                cur.PolicyRate = rate;
            }
            else
                cur.PolicyRate = Math.Clamp(taylor, b.Monetary.Floor, b.Monetary.Ceiling);
        }
        foreach (var cur in economy.Currencies.Values.Where(c => !c.IsFloating))
        {
            var anchor = economy.Currencies[cur.Anchor!];
            var observed = ObservedRate(world, cur.Home);
            cur.PegSpread = observed is { } rate ? rate - anchor.PolicyRate : b.Monetary.PegSpread;
            cur.PolicyRate = anchor.PolicyRate + cur.PegSpread;
            cur.Target = anchor.Target;
        }
        foreach (var cur in economy.Currencies.Values)
            foreach (var id in cur.Members)
            {
                // Нейтральная ставка страны: на старте ставка ЦБ не разгоняет и не тормозит экономику.
                // Так союз валют и привязка курса не дают стартового рывка странам с инфляцией, отличной от средней.
                var e = economy[id];
                e.PolicyRate = cur.PolicyRate;
                e.Neutral = Math.Clamp(cur.PolicyRate - e.Expectations, -3, 0.15);
            }

        // Разница реальных ставок с долларом на старте — от неё курс реагирует только на изменения.
        var reference = economy.Currencies.TryGetValue("USD", out var usd) ? usd : economy.Currencies.Values.First();
        var referenceReal = reference.PolicyRate - economy[reference.Home].Expectations;
        foreach (var cur in economy.Currencies.Values)
            cur.RealRateDifference = cur.PolicyRate - cur.Expectations - referenceReal;

        // Курс год назад: валюта дешевела на разницу инфляции с долларом (реальный курс постоянный).
        double Drift(CurrencyState cur) => cur.IsFloating
            ? (1 + cur.Inflation) / (1 + reference.Inflation)
            : Drift(economy.Currencies[cur.Anchor!]);
        foreach (var cur in economy.Currencies.Values)
        {
            var drift = Drift(cur);
            foreach (var id in cur.Members)
                for (var j = 0; j < 12; j++)
                    economy[id].ExchangeHistory[j] = Math.Pow(drift, -(12 - j) / 12.0);
        }
    }

    /// <summary>
    /// Привязка курса держится, только если инфляция близка к якорной валюте. Официальная привязка
    /// при инфляции на десятки процентов выше (как у кубинского песо) на деле не работает — курс считаем плавающим.
    /// </summary>
    static bool PegHolds(CurrencyRecord? record, WorldEconomy economy, List<string> members, MonetaryBalance b)
    {
        if (record is not { Regime: "peg", Anchor: { } anchor })
            return record is not null;
        var anchorMembers = economy.Countries.Where(kv => kv.Value.Currency == anchor).Select(kv => kv.Key).ToList();
        if (anchorMembers.Count == 0)
            return false;
        double Inflation(List<string> ids)
        {
            var w = Weights(economy, ids);
            return ids.Sum(id => w[id] * economy[id].Inflation);
        }
        return Inflation(members) - Inflation(anchorMembers) <= b.PegInflationGap;
    }

    static double? ObservedRate(World world, string country)
    {
        var stat = world[country].Info.Stats.GetValueOrDefault("policy_rate_pct");
        return stat is not null && stat.Source.StartsWith("bis:", StringComparison.Ordinal) ? stat.Value / 100 : null;
    }

    /// <summary>Веса стран внутри валюты по ВВП в долларах.</summary>
    public static Dictionary<string, double> Weights(WorldEconomy economy, IReadOnlyList<string> members)
    {
        var total = members.Sum(id => Math.Max(economy[id].GdpUsd, 1));
        return members.ToDictionary(id => id, id => Math.Max(economy[id].GdpUsd, 1) / total, StringComparer.Ordinal);
    }
}
