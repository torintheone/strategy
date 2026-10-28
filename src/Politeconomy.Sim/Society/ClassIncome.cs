using Politeconomy.Sim.Data;
using Politeconomy.Sim.Economy;
using Politeconomy.Sim.Economy.Systems;

namespace Politeconomy.Sim.Society;

/// <summary>
/// Доходы классов за год в местных единицах: зарплаты по отраслям и квалификации, прибыль, доходы от собственности,
/// пенсии и пособия из бюджета минус налоги; затем семья поддерживает пенсионеров и безработных. MODEL.md, «Классы».
/// </summary>
public static class ClassIncome
{
    /// <summary>Относительная зарплата работника класса: без образования, с профессиональным, с высшим.</summary>
    static readonly double[] WageWeights =
    [
        0, IndustryStructure.SkillWages[0], IndustryStructure.SkillWages[1], IndustryStructure.SkillWages[0], IndustryStructure.SkillWages[1],
        IndustryStructure.SkillWages[2], IndustryStructure.SkillWages[1], IndustryStructure.SkillWages[2], IndustryStructure.SkillWages[1], 0,
    ];

    /// <summary>Потоки по классам: зарплаты, прибыль и доходы от собственности, пенсии и пособия, налоги, итог после всего.</summary>
    public sealed record Flows(double[] Wages, double[] Profits, double[] Transfers, double[] Taxes, double[] Disposable);

    public static Flows Compute(GameData data, IReadOnlyList<ClassRecord> classes, CountryEconomy e, WorldEconomy economy,
        ClassComposition.Result c)
    {
        const int n = ClassIds.Count;
        var b = data.Society;
        var industries = data.Industries.Industries;
        var reference = EconomySystem.Reference(economy);
        var wages = new double[n];
        var profits = new double[n];
        var transfers = new double[n];
        var taxes = new double[n];

        // Зарплаты: фонд оплаты отрасли — доля труда в её добавленной стоимости, делится по квалификации.
        // Зарплата в отрасли не выше WagePremiumMax зарплат медианного работника страны: сверх — рента
        // (так бывает в добыче сырья и там, где в данных выпуск отрасли велик, а работников мало).
        var value = new double[industries.Count];
        var weight = new double[industries.Count];
        var rate = new double[industries.Count];
        for (var i = 0; i < industries.Count; i++)
        {
            value[i] = Math.Max(0, e.Output[i] * BudgetSystem.IndustryPrice(industries[i], e, economy, reference));
            for (var k = 0; k < n; k++)
                weight[i] += WageWeights[k] * c.WagePeople[i, k];
            rate[i] = weight[i] > 0 ? (1 - industries[i].Alpha) * value[i] / weight[i] : 0;
        }
        var wageCap = b.WagePremiumMax * MedianRate(rate, weight);
        double farmProfit = 0, businessProfit = 0;
        for (var i = 0; i < industries.Count; i++)
        {
            var ind = industries[i];
            if (value[i] <= 0)
                continue;
            var bill = Math.Min((1 - ind.Alpha) * value[i], wageCap * weight[i]);
            if (weight[i] > 0)
                for (var k = 0; k < n; k++)
                    wages[k] += bill * WageWeights[k] * c.WagePeople[i, k] / weight[i];
            // Прибыль — остаток минус износ (не меньше четверти доли капитала).
            var profit = Math.Max(0.25 * ind.Alpha * value[i], value[i] - bill - ind.Depreciation * e.Capital[i] * e.Price);
            if (ind.Id == "agriculture")
                farmProfit += profit;
            else
                businessProfit += profit;
        }

        // Силовики, мелкий бизнес и капитал уносят с собой зарплаты, которые получали бы в классах-донорах.
        var moved = 0.0;
        foreach (var k in ClassComposition.Donors)
        {
            moved += wages[k] * (1 - c.DonorScale);
            wages[k] *= c.DonorScale;
        }
        var movedWeight = c.Security * WageWeights[ClassIds.Officials] + c.SmallBusiness * WageWeights[ClassIds.SmallBusiness]
            + c.Capitalists * WageWeights[ClassIds.Capitalists];
        if (movedWeight > 0)
        {
            wages[ClassIds.Officials] += moved * c.Security * WageWeights[ClassIds.Officials] / movedWeight;
            wages[ClassIds.SmallBusiness] += moved * c.SmallBusiness * WageWeights[ClassIds.SmallBusiness] / movedWeight;
            wages[ClassIds.Capitalists] += moved * c.Capitalists * WageWeights[ClassIds.Capitalists] / movedWeight;
        }

        // Прибыль: крестьянам — их часть сельского хозяйства; остальное после налогов на прибыль, доходов
        // госпредприятий, природной ренты и доли иностранцев делят мелкий бизнес и крупный капитал,
        // а часть прибыли крупного бизнеса расходится всем как дивиденды, проценты и аренда.
        var revenue = Revenues(data, e);
        profits[ClassIds.Farmers] = b.FarmerProfitShare * farmProfit;
        // Налоги не забирают больше 70% прибыли (у малых стран бюджет бывает больше прибыли всех фирм).
        var business = businessProfit + (1 - b.FarmerProfitShare) * farmProfit;
        var pool = Math.Max(0.3 * business, business - revenue.Cit - revenue.Soe - revenue.Resource);
        pool *= 1 - EconomyBalance.ByIncome(b.ForeignProfitShare, e.IncomeIndex);
        var smallClaim = c.Heads[ClassIds.SmallBusiness];
        var bigClaim = c.Heads[ClassIds.Capitalists] * b.CapitalistWeight;
        var small = smallClaim + bigClaim > 0 ? pool * smallClaim / (smallClaim + bigClaim) : 0;
        profits[ClassIds.SmallBusiness] = small;
        var big = pool - small;
        profits[ClassIds.Capitalists] = b.CapitalistTake * big;
        var propertyWeight = 0.0;
        for (var k = 0; k < n; k++)
            propertyWeight += classes[k].Property * c.Heads[k];
        for (var k = 0; k < n; k++)
            profits[k] += propertyWeight > 0 ? (1 - b.CapitalistTake) * big * classes[k].Property * c.Heads[k] / propertyWeight : 0;

        // Пенсии и пособия: доля зарплаты (пенсия — средней, пособие — неквалифицированного рабочего) на получателя, но не больше своей доли расходов на соцзащиту;
        // остальное (детские, по инвалидности, жильё) — семьям поровну на человека. Безработные ещё подрабатывают.
        double wageSum = 0, workers = 0;
        for (var k = 0; k < n; k++)
            if (k is not (ClassIds.Unemployed or ClassIds.Pensioners))
            {
                wageSum += wages[k];
                workers += c.Heads[k];
            }
        var averageWage = workers > 0 ? wageSum / workers : 0;
        var social = revenue.SocialSpending;
        transfers[ClassIds.Pensioners] = Math.Min(b.PensionShare * social,
            EconomyBalance.ByIncome(b.PensionReplacement, e.IncomeIndex) * averageWage * c.Heads[ClassIds.Pensioners]);
        var unskilledWage = c.Heads[ClassIds.Unskilled] > 0 ? wages[ClassIds.Unskilled] / c.Heads[ClassIds.Unskilled] : averageWage;
        transfers[ClassIds.Unemployed] = Math.Min(b.BenefitShare * social,
            EconomyBalance.ByIncome(b.BenefitReplacement, e.IncomeIndex) * unskilledWage * c.Heads[ClassIds.Unemployed]);
        var families = c.Sizes.Sum() - c.Sizes[ClassIds.Pensioners];
        var familyBenefits = Math.Max(0, social - transfers[ClassIds.Pensioners] - transfers[ClassIds.Unemployed]);
        for (var k = 0; k < n; k++)
            if (k != ClassIds.Pensioners && families > 0)
                transfers[k] += familyBenefits * c.Sizes[k] / families;
        wages[ClassIds.Unemployed] = EconomyBalance.ByIncome(b.InformalEarnings, e.IncomeIndex) * unskilledWage * c.Heads[ClassIds.Unemployed];

        // Прямые налоги — по доходу с весами прогрессии (не больше 60% дохода класса), косвенные — со всех поровну.
        var gross = new double[n];
        for (var k = 0; k < n; k++)
            gross[k] = wages[k] + profits[k] + transfers[k];
        var taxBase = 0.0;
        for (var k = 0; k < n; k++)
            taxBase += b.TaxWeights[k] * gross[k];
        var disposable = new double[n];
        var total = 0.0;
        for (var k = 0; k < n; k++)
        {
            taxes[k] = taxBase > 0 ? Math.Min(0.6 * gross[k], revenue.Direct * b.TaxWeights[k] * gross[k] / taxBase) : 0;
            disposable[k] = gross[k] - taxes[k];
            total += disposable[k];
        }
        var indirect = total > 0 ? Math.Clamp(revenue.Indirect / total, 0, 0.5) : 0;
        for (var k = 0; k < n; k++)
        {
            taxes[k] += indirect * disposable[k];
            disposable[k] *= 1 - indirect;
        }

        // Семья кормит стариков и безработных: их доход на человека не ниже доли среднего.
        Support(disposable, c.Sizes, ClassIds.Pensioners, b.PensionerFloor);
        Support(disposable, c.Sizes, ClassIds.Unemployed, b.UnemployedFloor);
        return new Flows(wages, profits, transfers, taxes, disposable);
    }

    /// <summary>Зарплата медианного работника (на единицу квалификации): половина работников получает меньше.</summary>
    static double MedianRate(double[] rate, double[] weight)
    {
        var order = Enumerable.Range(0, rate.Length).Where(i => weight[i] > 0).OrderBy(i => rate[i]).ToList();
        var half = order.Sum(i => weight[i]) / 2;
        var seen = 0.0;
        foreach (var i in order)
        {
            seen += weight[i];
            if (seen >= half)
                return rate[i];
        }
        return double.PositiveInfinity;
    }

    static void Support(double[] income, double[] sizes, int k, double floor)
    {
        var population = sizes.Sum();
        if (population <= 0 || sizes[k] <= 0)
            return;
        var need = floor * income.Sum() / population * sizes[k] - income[k];
        if (need <= 0)
            return;
        var others = income.Sum() - income[k];
        if (others <= 0)
            return;
        for (var j = 0; j < income.Length; j++)
            if (j != k)
                income[j] -= need * income[j] / others;
        income[k] += need;
    }

    public sealed record RevenueParts(double Direct, double Indirect, double Cit, double Soe, double Resource, double SocialSpending);

    /// <summary>Доходы бюджета по группам: прямые налоги с людей, косвенные, с прибыли; расходы на пенсии и пособия.</summary>
    public static RevenueParts Revenues(GameData data, CountryEconomy e)
    {
        double direct = 0, indirect = 0, cit = 0, soe = 0, resource = 0;
        var revenues = data.Budget.Revenues;
        for (var k = 0; k < revenues.Count && k < e.Revenue.Length; k++)
        {
            var value = e.Revenue[k];
            switch (revenues[k].Id)
            {
                case "pit" or "social" or "property":
                    direct += value;
                    break;
                case "cit":
                    cit += value;
                    break;
                case "soe":
                    soe += value;
                    break;
                case "resource":
                    resource += value;
                    break;
                default:
                    indirect += value;
                    break;
            }
        }
        var spending = data.Budget.Spending;
        var social = 0.0;
        for (var k = 0; k < spending.Count && k < e.Spending.Length; k++)
            if (spending[k].Id == "social")
                social += e.Spending[k];
        return new RevenueParts(direct, indirect, cit, soe, resource, social);
    }
}
