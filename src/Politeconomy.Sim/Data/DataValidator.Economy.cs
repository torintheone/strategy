namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    static readonly string[] IncomeKeys = ["low", "lower_middle", "upper_middle", "high"];
    static readonly HashSet<string> Sectors = ["agriculture", "industry", "services"];
    static readonly HashSet<string> IndustryKinds = ["commodity", "manufactured", "service", "public"];
    static readonly HashSet<string> RevenueBases = ["wages", "profits", "consumption", "imports", "resources", "assets", "gdp"];

    /// <summary>Валюты, отрасли, статьи бюджета и коэффициенты экономики.</summary>
    static void ValidateEconomy(GameData data, Action<string, string> add)
    {
        // --- currencies.json
        var used = data.Countries.Countries.Select(c => c.Currency).ToHashSet(StringComparer.Ordinal);
        foreach (var c in data.Countries.Countries.Where(c => string.IsNullOrWhiteSpace(c.Currency)))
            add("countries.json", $"у страны {c.Id} не указана валюта");
        var currencies = new Dictionary<string, CurrencyRecord>(StringComparer.Ordinal);
        foreach (var cur in data.Currencies.Currencies)
        {
            if (!currencies.TryAdd(cur.Id, cur))
                add("currencies.json", $"валюта {cur.Id} описана дважды");
            if (cur.Regime is not ("float" or "peg"))
                add("currencies.json", $"валюта {cur.Id}: неизвестный режим «{cur.Regime}»");
            if (cur.Regime == "peg" && (cur.Anchor is null || !used.Contains(cur.Anchor)))
                add("currencies.json", $"валюта {cur.Id} привязана к «{cur.Anchor}», которой не пользуется ни одна страна");
        }
        foreach (var cur in data.Currencies.Currencies.Where(c => c.Regime == "peg" && c.Anchor is not null))
            if (currencies.TryGetValue(cur.Anchor!, out var anchor) && anchor.Regime == "peg")
                add("currencies.json", $"валюта {cur.Id} привязана к {cur.Anchor}, которая сама привязана к другой валюте");

        // --- industries.json
        var industries = data.Industries.Industries;
        if (industries.Count != 20)
            add("industries.json", $"отраслей {industries.Count}, должно быть 20");
        var industryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var i in industries)
        {
            if (!industryIds.Add(i.Id))
                add("industries.json", $"отрасль {i.Id} описана дважды");
            if (!Sectors.Contains(i.Sector))
                add("industries.json", $"отрасль {i.Id}: неизвестный сектор «{i.Sector}»");
            if (!IndustryKinds.Contains(i.Kind))
                add("industries.json", $"отрасль {i.Id}: неизвестный вид «{i.Kind}»");
            if (i.Alpha is <= 0.05 or >= 0.9)
                add("industries.json", $"отрасль {i.Id}: доля капитала {i.Alpha} вне 0,05…0,9");
            if (i.Depreciation is <= 0 or >= 0.3)
                add("industries.json", $"отрасль {i.Id}: выбытие капитала {i.Depreciation} вне 0…0,3");
            if (i.Skills.Count != 3 || Math.Abs(i.Skills.Sum() - 1) > 0.01)
                add("industries.json", $"отрасль {i.Id}: доли квалификаций должны быть три и в сумме давать 1");
        }
        foreach (var required in new[] { "agriculture", "oil_gas", "coal_ores", "defense", "construction", "tourism" })
            if (!industryIds.Contains(required))
                add("industries.json", $"нет отрасли {required} — она нужна для калибровки");
        foreach (var group in IncomeKeys)
        {
            var manufacturing = industries.Where(i => i.ManufacturingShare is not null).Sum(i => i.ManufacturingShare!.GetValueOrDefault(group));
            if (Math.Abs(manufacturing - 1) > 0.01)
                add("industries.json", $"доли обрабатывающих отраслей для группы {group} дают {manufacturing:F2}, а не 1");
        }
        var services = industries.Where(i => i.ServicesShare is not null).Sum(i => i.ServicesShare!.Value);
        if (services <= 0)
            add("industries.json", "нет долей отраслей в услугах (services_share)");

        // --- budget.json
        var revenueIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in data.Budget.Revenues)
        {
            if (!revenueIds.Add(r.Id))
                add("budget.json", $"доход {r.Id} описан дважды");
            if (!RevenueBases.Contains(r.Base))
                add("budget.json", $"доход {r.Id}: неизвестная база «{r.Base}»");
            if (r.Elasticity is < 0 or > 4)
                add("budget.json", $"доход {r.Id}: эластичность {r.Elasticity} вне 0…4");
        }
        if (!data.Budget.Revenues.Any(r => r.Base == "resources"))
            add("budget.json", "нет статьи доходов от природной ренты (base: resources)");
        if (!data.Budget.Spending.Any(s => s.Id == "defense"))
            add("budget.json", "нет статьи расходов defense — она нужна для калибровки по военным расходам");
        foreach (var group in IncomeKeys)
        {
            var revenue = data.Budget.Revenues.Sum(r => r.Shares.GetValueOrDefault(group));
            if (Math.Abs(revenue - 1) > 0.01)
                add("budget.json", $"доли доходов для группы {group} дают {revenue:F3}, а не 1");
            var spending = data.Budget.Spending.Sum(s => s.Shares.GetValueOrDefault(group));
            if (Math.Abs(spending - 1) > 0.01)
                add("budget.json", $"доли расходов для группы {group} дают {spending:F3}, а не 1");
        }

        // --- balance.json, раздел economy
        var e = data.Economy;
        if (data.Balance.Economy is null)
            add("balance.json", "нет раздела economy с коэффициентами модели");
        if (e.Demand.GapPersistence is <= 0 or >= 1)
            add("balance.json", "economy.demand.gap_persistence должен быть между 0 и 1");
        if (e.Monetary.Smoothing is <= 0 or > 1 || e.Prices.Adjustment is <= 0 or > 1 || e.Prices.Expectations is <= 0 or > 1)
            add("balance.json", "скорости подстройки (smoothing, adjustment, expectations) должны быть в (0, 1]");
        foreach (var (name, list) in new[]
                 {
                     ("demand.shock", e.Demand.Shock), ("prices.food_weight", e.Prices.FoodWeight), ("prices.credibility", e.Prices.Credibility),
                     ("monetary.neutral_rate", e.Monetary.NeutralRate), ("exchange.shock", e.Exchange.Shock),
                     ("budget.debt_anchor", e.Budget.DebtAnchor), ("budget.foreign_debt_share", e.Budget.ForeignDebtShare),
                     ("budget.stabilizers", e.Budget.Stabilizers),
                 })
            if (list.Count != 4)
                add("balance.json", $"economy.{name}: нужно 4 значения — по группам дохода");
    }
}
