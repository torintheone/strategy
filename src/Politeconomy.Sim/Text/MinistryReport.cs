using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;

namespace Politeconomy.Sim.Text;

/// <summary>
/// Доклад министра: по каждому показателю ведомства — значение, как было год-полтора назад и как у типичной страны мира
/// (медиана), с оценкой «лучше» или «хуже»; плюс замечания о рисках по показателям ведомства, совет министра и несколько
/// слов о нём самом. Слабый или нечестный министр приукрашивает нынешние цифры (прошлые и мировые — настоящие), и
/// замечания он пишет уже по приукрашенным; совет зависит от его взглядов, у слабого министра он плохой. В докладе
/// об этом ничего не сказано: узнать можно из газеты или сравнив с графиками. MODEL.md, «Доклады министров».
/// </summary>
public static class MinistryReport
{
    /// <summary>Показатели, у которых хорошо, когда они ниже (у остальных — когда выше).</summary>
    static readonly HashSet<string> LowerIsBetter =
    [
        "inflation_pct", "unemployment_pct", "gov_debt_pct_gdp", "corruption_index", "radicalism", "death_rate", "interest_pct_gdp",
        "bond_yield_pct", "infant_mortality", "disease_burden_pct", "crime_index", "pollution_index", "housing_price_index",
        "housing_shortage_pct", "power_shortage_pct", "electricity_price_index", "energy_import_dependence_pct", "brain_drain_pct",
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
        var spin = minister == null ? 0 : Spin(data.ReportsBalance, minister.Competence, minister.Corruption, minister.Traits);
        // Цифры, какими их видит министр в докладе.
        double Shown(string id)
        {
            var value = country.Stat(id)?.Value ?? double.NaN;
            return spin <= 0 || !double.IsFinite(value) ? value : Reported(data, id, value, Median(world, id), spin);
        }
        foreach (var id in post.Stats ?? [])
        {
            if (country.Stat(id) is not { } stat || !double.IsFinite(stat.Value))
                continue;
            var def = data.Indicator(id);
            stat = stat with { Value = Shown(id) };
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
        var warnings = Warnings(Shown, post.Stats ?? []).ToList();
        lines.AddRange(warnings);
        if (minister != null)
        {
            lines.Add("Совет министра: " + Lower(Advice(data, post, minister.Competence, minister.Views, warnings.Count > 0)));
            lines.Add(About(minister));
        }
        return lines;
    }

    static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    /// <summary>
    /// Насколько министр приукрашивает цифры (доля, 0 — честно): слабый — до WeakSpin, нечестный — до CorruptSpin,
    /// черты «коррупционер» и «популист» добавляют; всего не больше MaxSpin.
    /// </summary>
    public static double Spin(ReportsBalance b, double competence, double corruption, IReadOnlyList<string> traits)
    {
        var spin = b.WeakSpin * Math.Max(0, (b.HonestCompetence - competence) / Math.Max(1, b.HonestCompetence))
                   + b.CorruptSpin * Math.Max(0, (corruption - b.CorruptFrom) / Math.Max(1, 100 - b.CorruptFrom));
        if (traits.Contains("corrupt"))
            spin += b.TraitSpin;
        if (traits.Contains("populist"))
            spin += b.PopulistSpin;
        return Math.Clamp(spin, 0, b.MaxSpin);
    }

    /// <summary>Приукрашивается ли показатель: у нейтральных нет «лучше», рейтинг публикует агентство.</summary>
    public static bool Spinnable(GameData data, string id) => !Neutral.Contains(id) && data.Indicator(id).Format != "rating";

    /// <summary>
    /// Цифра в докладе: настоящая, сдвинутая в лучшую сторону на долю spin от того, насколько страна хуже типичной
    /// (но не меньше чем на десятую часть самого значения) — чем хуже дела, тем сильнее приукрашивание.
    /// Положительное значение не уходит ниже нуля, проценты и индексы — выше 100.
    /// </summary>
    public static double Reported(GameData data, string id, double value, double? median, double spin)
    {
        if (spin <= 0 || !double.IsFinite(value) || !Spinnable(data, id))
            return value;
        var better = LowerIsBetter.Contains(id) ? -1 : 1;
        var worse = Math.Max(0, better * ((median ?? value) - value));
        var shown = value + better * spin * Math.Max(worse, Math.Abs(value) * 0.1);
        if (value >= 0)
            shown = Math.Max(0, shown);
        if (value <= 100 && data.Indicator(id).Format is "percent" or "number0")
            shown = Math.Min(100, shown);
        return shown;
    }

    /// <summary>
    /// Совет министра: если в ведомстве нет проблем — продолжать курс; у слабого министра — плохой совет; иначе —
    /// по его взглядам на оси, важной для ведомства (слева — перераспределение, свободы, открытость; справа — рынок,
    /// порядок, суверенитет).
    /// </summary>
    public static string Advice(GameData data, PostRecord post, double competence, IReadOnlyList<double> views, bool problems)
    {
        if (post.Advice is not { } a)
            return data.Government.AdviceSteady;
        if (competence < data.ReportsBalance.BadAdviceCompetence)
            return a.Weak;
        if (!problems)
            return data.Government.AdviceSteady;
        return a.Axis < views.Count && views[a.Axis] < 0 ? a.Left : a.Right;
    }

    /// <summary>
    /// С каким годом сравнивать. История пишется 1 января за прошедший год, поэтому в начале года прошлый год почти
    /// совпадает с «сейчас»: до июля сравниваем с позапрошлым, с июля — с прошлым.
    /// </summary>
    public static int ReferenceYear(DateOnly date) => date.Month >= 7 ? date.Year - 1 : date.Year - 2;

    static bool Better(string id, double now, double before) => LowerIsBetter.Contains(id) ? now < before : now > before;

    public static double? Median(WorldSnapshot world, string id)
    {
        var values = world.Countries.Values.Select(c => c.Stat(id)?.Value ?? double.NaN).Where(double.IsFinite).OrderBy(v => v).ToList();
        return values.Count == 0 ? null : values[values.Count / 2];
    }

    /// <summary>Замечания о рисках — только по тем показателям, за которые отвечает ведомство.</summary>
    static IEnumerable<string> Warnings(Func<string, double> S, IReadOnlyList<string> stats)
    {
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
        if (Has("power_shortage_pct") && S("power_shortage_pct") > 1)
            yield return $"Не хватает {Fmt.Number(S("power_shortage_pct"), 1)}% электричества: веерные отключения останавливают заводы. Нужны новые станции и деньги на них.";
        else if (Has("power_shortage_pct") && S("power_reserve_pct") < 3)
            yield return "Запас мощности почти исчерпан: засушливый год или авария обернутся отключениями.";
        if (Has("infrastructure_index") && S("infrastructure_index") < 100)
            yield return "Инфраструктура перегружена: заводы и порты упираются в потолок. Нужны новые стройки и деньги на обслуживание.";
        if (Has("infrastructure_condition_pct") && S("infrastructure_condition_pct") < 55)
            yield return "Дороги и сети обветшали: без ремонта они выдерживают всё меньше.";
        if (Has("brain_drain_pct") && S("brain_drain_pct") > 3)
            yield return "Учёные и инженеры уезжают: им не хватает свободы или они недовольны жизнью. Наука работает хуже.";
        if (Has("crime_index") && S("crime_index") > 60)
            yield return "Преступность высокая: людям страшно выходить на улицу.";
        if (Has("pollution_index") && S("pollution_index") > 70)
            yield return "Воздух и вода сильно загрязнены: люди болеют и живут меньше.";
        if (Has("housing_price_index") && S("housing_shortage_pct") > 15)
            yield return "Жилья не хватает: цены растут, молодые семьи откладывают детей.";
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
