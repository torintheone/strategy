using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;

namespace Politeconomy.Sim.Text;

/// <summary>
/// Доклад министра: по каждому показателю ведомства — значение, как было год-полтора назад и как у типичной страны мира
/// (медиана), с оценкой «лучше» или «хуже»; плюс замечания о рисках по показателям ведомства и несколько слов
/// о самом министре.
/// </summary>
public static class MinistryReport
{
    /// <summary>Показатели, у которых хорошо, когда они ниже (у остальных — когда выше).</summary>
    static readonly HashSet<string> LowerIsBetter =
    [
        "inflation_pct", "unemployment_pct", "gov_debt_pct_gdp", "corruption_index", "radicalism", "death_rate", "interest_pct_gdp",
        "bond_yield_pct",
    ];

    /// <summary>Показатели, у которых нет «лучше» или «хуже» — только значение.</summary>
    static readonly HashSet<string> Neutral =
    [
        "population", "age_0_14_pct", "age_65_plus_pct", "birth_rate", "fertility_rate", "urban_pct", "exports_pct_gdp", "imports_pct_gdp",
        "agriculture_pct_gdp", "industry_pct_gdp", "manufacturing_pct_gdp", "employment_agriculture_pct", "resource_rents_pct_gdp",
        "military_pct_gdp", "gov_spending_pct_gdp", "gov_revenue_pct_gdp", "policy_rate_pct", "exchange_rate_change_pct",
        "net_migration_rate", "current_account_pct_gdp", "investment_pct_gdp",
    ];

    public static IReadOnlyList<string> Lines(GameData data, PostRecord post, CountrySnapshot country, WorldSnapshot world, CharacterSnapshot? minister)
    {
        var lines = new List<string>();
        foreach (var id in post.Stats ?? [])
        {
            if (country.Stat(id) is not { } stat || !double.IsFinite(stat.Value))
                continue;
            var def = data.Indicator(id);
            var text = $"{def.Name}: {Fmt.Value(def.Format, stat.Value, def.Unit)}";
            var year = ReferenceYear(world.Date);
            var before = (country.History?.Series(id) ?? []).Where(p => p.Year == year).Select(p => (double?)p.Value).FirstOrDefault();
            if (before is { } p)
            {
                var then = Fmt.Value(def.Format, p, def.Unit);
                text += $", в {year} году — {then}";
                // Оценка — только если разница видна в записи числа (рейтинг AA и AA — не «стало лучше»).
                if (!Neutral.Contains(id) && then != Fmt.Value(def.Format, stat.Value, def.Unit) && Math.Abs(stat.Value - p) > Math.Abs(p) * 0.01)
                    text += Better(id, stat.Value, p) ? " (стало лучше)" : " (стало хуже)";
            }
            var median = Median(world, id);
            if (median is { } m)
                text += $"; у типичной страны мира — {Fmt.Value(def.Format, m, def.Unit)}";
            lines.Add(text + ".");
        }
        lines.AddRange(Warnings(country, post.Stats ?? []));
        if (minister != null)
            lines.Add(About(minister));
        return lines;
    }

    /// <summary>
    /// С каким годом сравнивать. История пишется 1 января за прошедший год, поэтому в начале года прошлый год почти
    /// совпадает с «сейчас»: до июля сравниваем с позапрошлым, с июля — с прошлым.
    /// </summary>
    public static int ReferenceYear(DateOnly date) => date.Month >= 7 ? date.Year - 1 : date.Year - 2;

    static bool Better(string id, double now, double before) => LowerIsBetter.Contains(id) ? now < before : now > before;

    static double? Median(WorldSnapshot world, string id)
    {
        var values = world.Countries.Values.Select(c => c.Stat(id)?.Value ?? double.NaN).Where(double.IsFinite).OrderBy(v => v).ToList();
        return values.Count == 0 ? null : values[values.Count / 2];
    }

    /// <summary>Замечания о рисках — только по тем показателям, за которые отвечает ведомство.</summary>
    static IEnumerable<string> Warnings(CountrySnapshot c, IReadOnlyList<string> stats)
    {
        double S(string id) => c.Stat(id)?.Value ?? double.NaN;
        bool Has(string id) => stats.Contains(id);
        var inflation = S("inflation_pct");
        var target = S("inflation_anchor_pct");
        if (Has("inflation_pct") && inflation - target > 2)
            yield return $"Инфляция выше цели центробанка на {Fmt.Number(inflation - target, 1)} п. п.: ставку придётся держать высокой.";
        if (Has("unemployment_pct") && S("unemployment_pct") - S("unemployment_natural_pct") > 1.5)
            yield return "Безработица заметно выше обычного уровня: экономика недогружена.";
        if (Has("budget_balance_pct_gdp") && S("budget_balance_pct_gdp") < -4)
            yield return "Дефицит бюджета больше 4% ВВП — долг будет расти.";
        if (Has("gov_debt_pct_gdp") && S("gov_debt_pct_gdp") > 100)
            yield return "Госдолг больше годового ВВП: проценты съедают заметную часть доходов.";
        if (Has("satisfaction") && S("satisfaction") < 42)
            yield return "Люди недовольны жизнью — это грозит протестами.";
        if (Has("radicalism") && S("radicalism") > 45)
            yield return "Радикальные настроения сильны: недовольные готовы выйти на улицы.";
        if (Has("approval_pct") && S("approval_pct") < 35)
            yield return "Одобрение власти низкое.";
        if (Has("corruption_index") && S("corruption_index") > 65)
            yield return "Коррупция высокая: взятки берут и в министерствах, и в судах.";
    }

    static string About(CharacterSnapshot m)
    {
        if (m.Traits.Contains("corrupt"))
            return "Ходят слухи, что министр путает государственный карман со своим.";
        if (m.Loyalty < 35)
            return "Министр всё чаще спорит с вами и может перейти в оппозицию.";
        if (m.Competence >= 75)
            return "Министр уверен в цифрах: ведомство работает быстро, законы готовит в срок.";
        if (m.Competence < 35)
            return "Доклад подготовлен наспех: министру не хватает опыта, законы ведомство готовит медленно.";
        return "Министр докладывает спокойно: ведомство работает в обычном режиме.";
    }
}
