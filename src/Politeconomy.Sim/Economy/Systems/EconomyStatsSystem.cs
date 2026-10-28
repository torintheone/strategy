using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>
/// Переносит итоги месяца в показатели стран (досье, карта, верхняя панель) вместе с разбором,
/// из чего сложилось значение, и записывает месяц в историю. Показатели, которые модель пока не считает
/// (продолжительность жизни, неравенство…), остаются из данных.
/// </summary>
public sealed class EconomyStatsSystem : EconomySystem
{
    public override string Name => "Показатели экономики";
    public override SimPhase Phase => SimPhase.Budget;

    protected override void Run(World world, WorldEconomy economy, EconomyBalance balance)
    {
        Publish(world);
        foreach (var country in world.Countries)
            economy[country.Id].PushHistory();
        economy.PushHistory();
    }

    /// <summary>Записать показатели из состояния модели. Ничего не меняет, кроме Stats.</summary>
    public static void Publish(World world)
    {
        var economy = world.Economy ?? throw new InvalidOperationException("Экономика не создана");
        var data = world.Data;
        var industries = data.Industries.Industries;
        var year = world.Date.Year;
        var reference = economy[economy.Currencies.TryGetValue("USD", out var usd) ? usd.Home : world.Countries[0].Id];
        var labor = data.Economy.Labor;

        foreach (var country in world.Countries)
        {
            var e = economy[country.Id];
            var stats = country.Stats;
            var y = e.NominalGdp;
            void Set(string id, double value, params StatFactor[] factors) =>
                stats[id] = new StatValue(value, year, StatSources.Model, null, null, factors.Length > 0 ? factors : null);
            static StatFactor F(string label, double value, string format = "percent_signed") => new(label, value * 100, format);

            Set("population", e.Population);
            Set("labor_force", e.LaborForce);
            Set("gdp_usd", e.GdpUsd);
            Set("gdp_per_capita_usd", e.GdpUsd / e.Population);
            Set("gdp_ppp_per_capita", e.Ppp0 * (e.RealGdp / e.RealGdp0) / (e.Population / e.Population0) * reference.Price);

            var growth = e.RealGdp / e.YearAgo(e.RealGdpHistory) - 1;
            Set("gdp_growth_pct", growth * 100,
                F("Рост потенциала экономики", e.PotentialGrowth),
                F("Отклонение от потенциала (подъём или спад)", e.Gap));

            var inflation = e.Price / e.YearAgo(e.PriceHistory) - 1;
            Set("inflation_pct", inflation * 100,
                F("Инфляционные ожидания", e.Expectations, "percent"),
                F("Перегрев или спад экономики", e.DemandPart),
                F("Курс валюты и цены импорта", e.ImportPart),
                F("Продовольствие и энергия", e.CommodityPart),
                F("Цель центробанка", e.InflationTarget, "percent"));

            Set("unemployment_pct", e.Unemployment * 100,
                F("Естественная безработица", e.NaturalUnemployment, "percent"),
                F("Влияние подъёма или спада", -labor.Okun * e.Gap));

            var cur = economy.Currencies[e.Currency];
            if (cur.IsFloating)
                Set("policy_rate_pct", e.PolicyRate * 100,
                    F("Нейтральная реальная ставка", cur.Neutral, "percent"),
                    F("Инфляция в зоне валюты", cur.Inflation, "percent"),
                    F("Отклонение инфляции от цели", cur.Inflation - cur.Target),
                    F("Разрыв выпуска", cur.Gap));
            else
                Set("policy_rate_pct", e.PolicyRate * 100,
                    F("Ставка якорной валюты " + cur.Anchor, economy.Currencies[cur.Anchor!].PolicyRate, "percent"),
                    F("Надбавка за привязку курса", cur.PegSpread));

            var nominalGrowth = (1 + growth) * (1 + inflation) - 1;
            Set("gov_debt_pct_gdp", e.DebtRatio * 100,
                F("Средняя ставка по долгу", e.EffectiveRate, "percent"),
                F("Ставка по новым займам", e.MarketRate, "percent"),
                F("Рост номинального ВВП", nominalGrowth),
                F("Доля долга в иностранной валюте", e.ForeignDebtShare, "percent"));

            var revenue = e.TotalRevenue;
            var spending = e.TotalSpending;
            Set("budget_balance_pct_gdp", e.BalanceRatio * 100,
                F("Доходы", revenue / y, "percent"),
                F("Расходы без процентов", spending / y, "percent"),
                F("Проценты по долгу", e.Interest / y, "percent"));
            Set("gov_revenue_pct_gdp", revenue / y * 100,
                data.Budget.Revenues.Select((r, k) => F(r.Name, e.Revenue[k] / y, "percent")).Where(f => f.Value >= 0.05).ToArray());
            Set("gov_spending_pct_gdp", (spending + e.Interest) / y * 100,
                data.Budget.Spending.Select((s, k) => F(s.Name, e.Spending[k] / y, "percent"))
                    .Append(F("Обслуживание долга", e.Interest / y, "percent")).Where(f => f.Value >= 0.05).ToArray());
            Set("interest_pct_gdp", e.Interest / y * 100,
                F("Средняя ставка по долгу", e.EffectiveRate, "percent"),
                F("Госдолг", e.DebtRatio, "percent"));
            var defense = data.Budget.Spending.Select((s, k) => (s, k)).Where(t => t.s.Id == "defense").Sum(t => e.Spending[t.k]);
            Set("military_pct_gdp", defense / y * 100);

            Set("current_account_pct_gdp", e.CurrentAccount * 100,
                F("Экспорт", e.Exports, "percent"),
                F("Импорт", e.Imports, "percent"));
            Set("exports_pct_gdp", e.Exports * 100);
            Set("imports_pct_gdp", e.Imports * 100);
            Set("investment_pct_gdp", e.InvestmentRate * 100);
            if (e.Defaults > 0)
                Set("credit_rating", e.Rating, new StatFactor($"Дефолтов с начала партии (последний — в {e.LastDefaultYear} году)", e.Defaults, "number0"));
            else
                Set("credit_rating", e.Rating);
            Set("defaults", e.Defaults);
            Set("bond_yield_pct", e.MarketRate * 100,
                F("Ключевая ставка", e.PolicyRate, "percent"),
                F("Премия за риск по рейтингу", e.Spread, "percent"));
            Set("exchange_rate_change_pct", (e.ExchangeRate / e.YearAgo(e.ExchangeHistory) - 1) * 100);

            // Структура ВВП в текущих ценах: при дорогой нефти доля добычи растёт.
            double agriculture = 0, industry = 0, services = 0, manufacturing = 0;
            for (var i = 0; i < industries.Count; i++)
            {
                var value = e.Output[i] * BudgetSystem.IndustryPrice(industries[i], e, economy, reference);
                switch (industries[i].Sector)
                {
                    case "agriculture": agriculture += value; break;
                    case "industry": industry += value; break;
                    default: services += value; break;
                }
                if (industries[i].Kind == "manufactured")
                    manufacturing += value;
            }
            var total = agriculture + industry + services;
            if (total > 0)
            {
                Set("agriculture_pct_gdp", agriculture / total * 100);
                Set("industry_pct_gdp", industry / total * 100);
                Set("services_pct_gdp", services / total * 100);
                Set("manufacturing_pct_gdp", manufacturing / total * 100);
            }
        }
    }
}
