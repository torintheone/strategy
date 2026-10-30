using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws.Systems;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Laws;

/// <summary>Законы: варианты на старте, расчёт эффектов, системы в порядке MODEL.md.</summary>
public static class LawModel
{
    public static IReadOnlyList<ISimSystem> Systems() =>
    [
        new LawEffectsSystem(),
        new LegislationSystem(),
        new InstitutionsSystem(),
    ];

    public static void Setup(World world)
    {
        var data = world.Data;
        var laws = new WorldLaws();
        foreach (var country in world.Countries)
        {
            var c = new CountryLaws { Attitudes = new double[data.Classes.Classes.Count] };
            foreach (var law in data.Laws.Laws)
                c.Current[law.Id] = c.Start[law.Id] = StartVariant(law, country);
            laws.Countries[country.Id] = c;
        }
        world.Laws = laws;
        InstitutionsSystem.SetupStart(world);
        InstitutionsSystem.Publish(world);
    }

    /// <summary>Вариант закона на старте: страна, затем режим, тип системы, группа дохода, по умолчанию.</summary>
    public static string StartVariant(LawRecord law, Country country)
    {
        var s = law.Start;
        if (s.Countries?.GetValueOrDefault(country.Id) is { } byCountry)
            return byCountry;
        if (s.Regime?.GetValueOrDefault(WorldFactory.RegimeId(country.Regime)) is { } byRegime)
            return byRegime;
        if (s.System?.GetValueOrDefault(WorldFactory.SystemId(country.System)) is { } bySystem)
            return bySystem;
        if (s.Income?.GetValueOrDefault(WorldFactory.IncomeId(country.Income)) is { } byIncome)
            return byIncome;
        return s.Default;
    }

    /// <summary>Эффекты, которые модель умеет применять (кроме семейств revenue.*, spending.*, organization.*, index.*).</summary>
    public static readonly IReadOnlySet<string> KnownEffects = new HashSet<string>(StringComparer.Ordinal)
    {
        "natural_unemployment", "potential_growth", "credibility", "inflation_target", "monetary_control", "food_inflation",
        "exchange_volatility", "spread", "pension_replacement", "benefit_replacement", "unskilled_wage", "tax_progressivity",
        "unemployment_fear", "small_business", "tertiary", "vocational", "foreign_profit", "capitalist_take",
        "older_participation", "fertility", "migration", "life_expectancy", "radicalism", "legitimacy",
        "protest_rate", "protest_escalation", "disperse", "health_access", "health_capacity", "disease_burden",
    };

    /// <summary>Индексы институтов, которые законы могут сдвигать (index.*).</summary>
    public static readonly IReadOnlySet<string> KnownIndices = new HashSet<string>(StringComparer.Ordinal)
    {
        "democracy", "rule_of_law", "judicial_independence", "press_freedom", "civil_liberties", "corruption",
    };

    /// <summary>Модель умеет применять эффект с таким ключом.</summary>
    public static bool IsKnownEffect(GameData data, string key)
    {
        if (KnownEffects.Contains(key))
            return true;
        var dot = key.IndexOf('.');
        if (dot < 0)
            return false;
        var (family, id) = (key[..dot], key[(dot + 1)..]);
        return family switch
        {
            "revenue" => data.Budget.Revenues.Any(r => r.Id == id),
            "spending" => data.Budget.Spending.Any(s => s.Id == id),
            "organization" => data.Classes.Classes.Any(c => c.Id == id),
            "index" => KnownIndices.Contains(id),
            _ => false,
        };
    }

    /// <summary>Центробанк страны подчинён правительству (закон «Независимость центробанка»).</summary>
    public static bool CentralBankObeys(World world, string country) =>
        world.Laws?.Countries.GetValueOrDefault(country)?.Current.GetValueOrDefault("central_bank") == "government";

    /// <summary>
    /// Поправка к модели по ключу эффекта: от законов, давления групп интересов, протестов и репрессий, человеческого
    /// капитала, здоровья и старения, программ ведомств (0, если поправок нет).
    /// </summary>
    public static double Effect(World world, string country, string key) =>
        (world.Laws?.Countries.GetValueOrDefault(country)?.Effect(key) ?? 0)
        + (world.Politics?.Countries.GetValueOrDefault(country) is { } p
            ? p.GroupEffects.GetValueOrDefault(key) + p.ProtestEffects.GetValueOrDefault(key)
            : 0)
        + (world.Education?.Countries.GetValueOrDefault(country)?.Effects.GetValueOrDefault(key) ?? 0)
        + (world.Health?.Countries.GetValueOrDefault(country)?.Effects.GetValueOrDefault(key) ?? 0)
        + (world.Programs?.Countries.GetValueOrDefault(country)?.Effects.GetValueOrDefault(key) ?? 0);

    /// <summary>
    /// Пересчитать поправки страны: для каждого закона — эффект нынешнего варианта (в переходный период — между
    /// прежним и новым) минус эффект варианта на старте. Отношение классов меняется сразу после принятия.
    /// </summary>
    public static void Recompute(GameData data, CountryLaws c, DateOnly date)
    {
        var classes = data.Classes.Classes;
        c.Modifiers.Clear();
        if (c.Attitudes.Length != classes.Count)
            c.Attitudes = new double[classes.Count];
        Array.Clear(c.Attitudes);
        foreach (var law in data.Laws.Laws)
        {
            var current = c.Current[law.Id];
            var start = c.Start[law.Id];
            var transition = c.Transitions.FirstOrDefault(t => t.Law == law.Id);
            if (current == start && transition == null)
                continue;
            var startVariant = law.Variant(start);
            var to = law.Variant(current);
            var from = transition == null ? to : law.Variant(transition.From);
            var progress = transition?.Progress(date) ?? 1;
            foreach (var key in to.Effects.Keys.Union(from.Effects.Keys).Union(startVariant.Effects.Keys))
            {
                var value = from.Effects.GetValueOrDefault(key) + (to.Effects.GetValueOrDefault(key) - from.Effects.GetValueOrDefault(key)) * progress
                            - startVariant.Effects.GetValueOrDefault(key);
                if (value != 0)
                    c.Modifiers[key] = c.Modifiers.GetValueOrDefault(key) + value;
            }
            for (var k = 0; k < classes.Count; k++)
                c.Attitudes[k] += to.Attitudes.GetValueOrDefault(classes[k].Id) - startVariant.Attitudes.GetValueOrDefault(classes[k].Id);
        }
        c.Transitions.RemoveAll(t => t.Progress(date) >= 1);
    }
}
