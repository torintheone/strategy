using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Science;

/// <summary>
/// Наука и технологии. Очки исследований (% ВВП в год) — государственные расходы на науку и частные исследования ×
/// эффективность (специалисты, качество образования, свободы, утечка мозгов). Очки идут по ветвям — на самую раннюю
/// неосвоенную технологию ветви — и на приоритетную технологию. Технология, которую освоил почти весь мир, обходится
/// дешевле: догоняющие страны осваивают известное, лидеры двигают границу дорого. Технологии дают производительность
/// секторов и другие эффекты; производительность и продолжительность жизни — в сравнении с миром: преимущество тает,
/// когда технологию осваивают другие. Рост от усилий на исследования — относительно старта. MODEL.md, «Наука».
/// </summary>
public static class ScienceModel
{
    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.ScienceSystem()];

    /// <summary>Эффекты, у которых считается преимущество перед миром: вклад технологии × (1 − доля мира, которая её освоила).</summary>
    public static bool Relative(string key) =>
        key.StartsWith("productivity.", StringComparison.Ordinal) || key is "life_expectancy" or "disease_burden";

    public static void Setup(World world)
    {
        var data = world.Data;
        var techs = data.Science.Technologies.OrderBy(t => t.Tier).ToList();
        var science = new WorldScience();
        world.Science = science;
        foreach (var country in world.Countries)
        {
            var c = new CountryScience
            {
                Allocation = Enumerable.Repeat(1.0 / data.Science.Branches.Count, data.Science.Branches.Count).ToArray(),
                Specialists0 = Specialists(world, country), Quality0 = Quality(world, country), Freedom0 = Freedom(country),
                Investment0 = world.Economy![country.Id].InvestmentRate,
            };
            var index = Index(world, country);
            foreach (var t in techs)
                if (data.ScienceBalance.Start[Math.Clamp(t.Tier - 1, 0, data.ScienceBalance.Start.Count - 1)] <= index && Ready(data, c, t))
                {
                    c.Researched.Add(t.Id);
                    c.Start.Add(t.Id);
                }
            c.Drain0 = c.Drain = Drain(world, country);
            c.Spending0 = Government(world, country, null) + Private(world, country);
            c.Points = Points(world, country, c);
            science.Countries[country.Id] = c;
        }
        Spread(world);
        foreach (var country in world.Countries)
        {
            var c = science[country.Id];
            foreach (var (key, value) in Contributions(world, c))
                if (Relative(key))
                    c.Base[key] = value;
        }
        Spread(world);
        foreach (var country in world.Countries)
            Apply(world, country, science[country.Id]);
    }

    /// <summary>Индекс развития (0–100) по ВВП на душу по ППС: от него зависит, какие технологии страна знает на старте.</summary>
    public static double Index(World world, Country country)
    {
        var b = world.Data.ScienceBalance;
        var ppp = Stat(country, "gdp_ppp_per_capita", b.IndexFloor);
        return Math.Clamp(100 * Math.Log(Math.Max(1, ppp) / b.IndexFloor) / Math.Log(b.IndexCeiling / b.IndexFloor), 0, 100);
    }

    /// <summary>Месяц: эффективность, очки, усилия к старту; очки — приоритетной технологии и по ветвям.</summary>
    public static void Update(World world, Country country, CountryScience c)
    {
        var b = world.Data.ScienceBalance;
        c.Drain = Drain(world, country);
        c.Efficiency = Math.Clamp(Math.Pow(Ratio(Specialists(world, country), c.Specialists0), b.EfficiencySpecialists)
                                  * Math.Pow(Ratio(Quality(world, country), c.Quality0), b.EfficiencyQuality)
                                  * Math.Pow(Ratio(Freedom(country), c.Freedom0), b.EfficiencyFreedom)
                                  * Math.Max(0.1, 1 - (c.Drain - c.Drain0)) * Math.Max(0.1, 1 + LawModel.Effect(world, country.Id, "research")), 0.1, 3);
        c.Points = Points(world, country, c);
        var effort = c.Spending0 > 0 ? (Government(world, country, c) + Private(world, country)) / c.Spending0 : 1;
        c.Effort += (effort - c.Effort) / (12 * b.EffortYears);

        // Очки за месяц — в миллиардах долларов: открывать новое могут только те, у кого много денег на науку.
        var month = c.Points / 100 * Gdp(world, country) / 12;
        var data = world.Data;
        if (c.Priority != null && data.Science.Technologies.FirstOrDefault(t => t.Id == c.Priority) is { } priority && Available(data, c, priority))
        {
            Invest(world, country, c, priority, month * b.PriorityShare);
            month *= 1 - b.PriorityShare;
        }
        else
            c.Priority = null;
        var branches = data.Science.Branches;
        var targets = branches.Select(br => Target(data, c, br.Id)).ToArray();
        var weight = Enumerable.Range(0, branches.Count).Where(k => targets[k] != null).Sum(k => c.Allocation[k]);
        if (weight <= 0)
        {
            // Всё распределено в ветви, где нечего изучать, — очки делятся поровну между остальными.
            weight = targets.Count(t => t != null);
            for (var k = 0; k < branches.Count; k++)
                if (targets[k] is { } t)
                    Invest(world, country, c, t, month / weight);
            return;
        }
        for (var k = 0; k < branches.Count; k++)
            if (targets[k] is { } t && c.Allocation[k] > 0)
                Invest(world, country, c, t, month * c.Allocation[k] / weight);
    }

    static void Invest(World world, Country country, CountryScience c, TechnologyRecord t, double points)
    {
        var progress = c.Progress.GetValueOrDefault(t.Id) + points;
        if (progress < Cost(world, country, t))
        {
            c.Progress[t.Id] = progress;
            return;
        }
        c.Progress.Remove(t.Id);
        c.Researched.Add(t.Id);
        if (c.Priority == t.Id)
            c.Priority = null;
        var science = world.Science!;
        var first = !science.Countries.Values.Any(x => !ReferenceEquals(x, c) && x.Researched.Contains(t.Id));
        if (first && !science.Discoveries.ContainsKey(t.Id))
            science.Discoveries[t.Id] = new Discovery(country.Id, world.Date);
        if (first)
        {
            world.Log(country.Id, "science", $"Впервые в мире: учёные освоили технологию «{t.Name}».", "science_first");
            // О чужом открытии узнаёт и игрок: теперь эту технологию дешевле осваивать.
            if (world.PlayerCountry is { } player && player != country.Id)
                world.Log(player, "science", $"{country.Name}: впервые в мире освоена технология «{t.Name}». Теперь её дешевле освоить и нам.", News.NewsModel.None);
        }
        else if (world.PlayerCountry == country.Id)
            world.Log(country.Id, "science", $"Учёные освоили технологию «{t.Name}».");
    }

    /// <summary>Самая ранняя технология ветви, которую можно изучать.</summary>
    public static TechnologyRecord? Target(GameData data, CountryScience c, string branch) =>
        data.Science.Technologies.Where(t => t.Branch == branch && Available(data, c, t)).OrderBy(t => t.Tier).FirstOrDefault();

    /// <summary>Можно изучать: не освоена, освоены предыдущий уровень ветви и всё, что нужно ещё.</summary>
    public static bool Available(GameData data, CountryScience c, TechnologyRecord t) => !c.Researched.Contains(t.Id) && Ready(data, c, t);

    static bool Ready(GameData data, CountryScience c, TechnologyRecord t) =>
        (t.Tier <= 1 || data.Science.Technologies.Where(x => x.Branch == t.Branch && x.Tier == t.Tier - 1).All(x => c.Researched.Contains(x.Id)))
        && (t.Requires ?? []).All(c.Researched.Contains);

    /// <summary>
    /// Цена (млрд долларов исследований). Открыть новое — одна цена для всех: это под силу только крупным лидерам.
    /// Освоить известное — доля своего ВВП, тем меньшая, чем больше мира уже владеет технологией, но не дороже открытия.
    /// </summary>
    public static double Cost(World world, Country country, TechnologyRecord t)
    {
        var b = world.Data.ScienceBalance;
        var tier = b.TierCost[Math.Clamp(t.Tier - 1, 0, b.TierCost.Count - 1)];
        var adoption = world.Science?.Adoption.GetValueOrDefault(t.Id) ?? 0;
        var frontier = b.FrontierCost * tier;
        // Научное сотрудничество: технология, которая уже есть у партнёра, дешевле.
        var bal = world.Data.DiplomacyBalance;
        var partners = Diplomacy.DiplomacyModel.SciencePartners(world, country.Id).Count(p => world.Science?.Countries.GetValueOrDefault(p)?.Researched.Contains(t.Id) == true);
        var discount = 1 - Math.Min(bal.ScienceMax, bal.ScienceDiscount * partners);
        return adoption <= 0 ? frontier : Math.Min(frontier, b.BaseCost / 100 * tier * Gdp(world, country) * (1 - b.Diffusion * adoption)) * discount;
    }

    /// <summary>ВВП страны по ППС, млрд долларов.</summary>
    static double Gdp(World world, Country country) => SocietyModel.PppGdp(world.Economy![country.Id]) / 1e9;

    /// <summary>Очки исследований в год (% ВВП): государственные расходы на науку и частные исследования × эффективность.</summary>
    static double Points(World world, Country country, CountryScience c) => (Government(world, country, c) + Private(world, country)) * c.Efficiency;

    /// <summary>
    /// Государственные расходы на науку, % ВВП (без денег программ и строек). Автопилот бюджета меняет все статьи разом,
    /// не думая о науке, поэтому у стран без игрока доля науки остаётся стартовой.
    /// </summary>
    public static double Government(World world, Country country, CountryScience? c)
    {
        var k = world.Data.Budget.Spending.ToList().FindIndex(s => s.Id == "science");
        var e = world.Economy![country.Id];
        var programs = (world.Programs?.Countries.GetValueOrDefault(country.Id)?.Effects.GetValueOrDefault("spending.science") ?? 0)
                       + (world.Infrastructure?.Countries.GetValueOrDefault(country.Id)?.Effects.GetValueOrDefault("spending.science") ?? 0);
        var spending = k < 0 || k >= e.SpendingPlan.Length ? 0 : Math.Max(0, e.SpendingPlan[k] * 100 + LawModel.Effect(world, country.Id, "spending.science") - programs);
        if (c != null && world.PlayerCountry != country.Id)
            spending = Math.Max(0, c.Spending0 - Private(world, country));
        return spending;
    }

    /// <summary>Частные исследования, % ВВП: по группе дохода.</summary>
    public static double Private(World world, Country country)
    {
        var b = world.Data.ScienceBalance;
        return b.Private[Math.Clamp((int)country.Income, 0, b.Private.Count - 1)];
    }

    static double Specialists(World world, Country country) =>
        world.Society?.Countries.GetValueOrDefault(country.Id) is { } s && s.Total > 0 ? s.Size[ClassIds.Professionals] / s.Total : 0.1;

    static double Quality(World world, Country country) => world.Education?.Countries.GetValueOrDefault(country.Id)?.Quality ?? 50;

    static double Freedom(Country country) => (Stat(country, "civil_liberties_index", 50) + Stat(country, "press_freedom_index", 50)) / 2;

    /// <summary>Утечка мозгов (доля исследователей, уезжающих за год): от недовольства специалистов и несвободы.</summary>
    static double Drain(World world, Country country)
    {
        var b = world.Data.ScienceBalance;
        var mood = world.Society?.Countries.GetValueOrDefault(country.Id)?.Satisfaction[ClassIds.Professionals] ?? 50;
        return b.DrainMood * Math.Max(0, 50 - mood) / 50 + b.DrainFreedom * Math.Max(0, 60 - Stat(country, "civil_liberties_index", 60)) / 60;
    }

    static double Ratio(double now, double start) => start > 0 && now > 0 ? now / start : 1;

    /// <summary>Доля мира (по ВВП по ППС), освоившая каждую технологию; кто открыл новое первым.</summary>
    public static void Spread(World world)
    {
        var science = world.Science!;
        var weights = world.Countries.ToDictionary(c => c.Id, c => SocietyModel.PppGdp(world.Economy![c.Id]), StringComparer.Ordinal);
        var total = weights.Values.Sum();
        foreach (var t in world.Data.Science.Technologies)
            science.Adoption[t.Id] = total > 0
                ? science.Countries.Where(kv => kv.Value.Researched.Contains(t.Id)).Sum(kv => weights.GetValueOrDefault(kv.Key)) / total
                : 0;
        // Средний по миру выигрыш (по ВВП): производительность и здоровье считаются в сравнении с ним, и в сумме по миру
        // технологии их не меняют — общий прогресс уже заложен в прогнозы.
        var mean = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, c) in science.Countries)
        {
            var w = weights.GetValueOrDefault(id);
            foreach (var (key, value) in Contributions(world, c))
                mean[key] = mean.GetValueOrDefault(key) + w * (value - c.Base.GetValueOrDefault(key));
        }
        science.Mean = mean.ToDictionary(kv => kv.Key, kv => total > 0 ? kv.Value / total : 0, StringComparer.Ordinal);
    }

    /// <summary>Вклад освоенных технологий по ключам: у относительных — × (1 − доля мира, которая её освоила).</summary>
    static Dictionary<string, double> Contributions(World world, CountryScience c)
    {
        var sum = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var t in world.Data.Science.Technologies)
        {
            if (!c.Researched.Contains(t.Id))
                continue;
            var spread = world.Science!.Adoption.GetValueOrDefault(t.Id);
            foreach (var (key, value) in t.Effects)
                sum[key] = sum.GetValueOrDefault(key) + (Relative(key) ? value * (1 - spread) : 0);
        }
        return sum;
    }

    /// <summary>
    /// Поправки: производительность секторов и продолжительность жизни — преимущество перед миром к старту; остальные
    /// эффекты — от технологий, освоенных после старта; рост — от усилий на исследования к старту.
    /// </summary>
    public static void Apply(World world, Country country, CountryScience c)
    {
        var contributions = Contributions(world, c);
        c.Productivity.Clear();
        c.Effects.Clear();
        foreach (var key in contributions.Keys.Union(c.Base.Keys).ToList())
        {
            var gain = contributions.GetValueOrDefault(key) - c.Base.GetValueOrDefault(key) - world.Science!.Mean.GetValueOrDefault(key);
            if (key.StartsWith("productivity.", StringComparison.Ordinal))
                c.Productivity[key["productivity.".Length..]] = Math.Exp(gain);
            else
                c.Effects[key] = gain;
        }
        foreach (var t in world.Data.Science.Technologies)
        {
            if (!c.Researched.Contains(t.Id) || c.Start.Contains(t.Id))
                continue;
            foreach (var (key, value) in t.Effects)
                if (!Relative(key))
                    c.Effects[key] = c.Effects.GetValueOrDefault(key) + value;
        }
        c.Effects["potential_growth"] = world.Data.ScienceBalance.ScienceGrowth * Math.Log(Math.Max(0.1, c.Effort));
        Publish(world, country, c);
    }

    /// <summary>Множитель производительности отрасли от технологий (1 — как на старте).</summary>
    public static double Productivity(World world, string country, int industry)
    {
        if (world.Science?.Countries.GetValueOrDefault(country) is not { } c)
            return 1;
        var industries = world.Data.Industries.Industries;
        return industry < industries.Count ? c.Productivity.GetValueOrDefault(industries[industry].Sector, 1) : 1;
    }

    /// <summary>Распределить очки: доля ветви меняется на delta, остальные — пропорционально (только в своей стране).</summary>
    public static bool Allocate(World world, string countryId, string branch, double delta)
    {
        if (world.PlayerCountry != countryId || world.Science?.Countries.GetValueOrDefault(countryId) is not { } c)
            return false;
        var k = world.Data.Science.Branches.ToList().FindIndex(x => x.Id == branch);
        if (k < 0)
            return false;
        var a = c.Allocation;
        var value = Math.Clamp(a[k] + delta, 0, 1);
        var others = a.Sum() - a[k];
        for (var i = 0; i < a.Length; i++)
            if (i != k)
                a[i] = others > 1e-9 ? a[i] / others * (1 - value) : (1 - value) / (a.Length - 1);
        a[k] = value;
        return true;
    }

    /// <summary>Выбрать приоритетную технологию (null — снять); только в своей стране и только ту, что можно изучать.</summary>
    public static bool Prioritize(World world, string countryId, string? technology)
    {
        if (world.PlayerCountry != countryId || world.Science?.Countries.GetValueOrDefault(countryId) is not { } c)
            return false;
        if (technology == null)
        {
            c.Priority = null;
            return true;
        }
        if (world.Data.Science.Technologies.FirstOrDefault(t => t.Id == technology) is not { } t || !Available(world.Data, c, t))
            return false;
        c.Priority = technology;
        world.Log(countryId, "science", $"Приоритет учёных — технология «{t.Name}»: на неё идёт половина всех очков исследований.");
        return true;
    }

    static double Stat(Country country, string id, double fallback) =>
        country.Stat(id) is var v && double.IsFinite(v) ? v
        : country.Info.Stats.TryGetValue(id, out var data) && double.IsFinite(data.Value) ? data.Value : fallback;

    static void Publish(World world, Country country, CountryScience c)
    {
        var year = world.Date.Year;
        void Set(string id, double value, params StatFactor[] factors) =>
            country.Stats[id] = new StatValue(value, year, StatSources.Model, null, null, factors.Length > 0 ? factors : null);
        var known = world.Science!.Adoption.Values.Count(a => a > 0);
        Set("research_effort_pct_gdp", c.Points,
            new StatFactor("Государство, % ВВП", Government(world, country, c), "number2"),
            new StatFactor("Бизнес, % ВВП", Private(world, country), "number2"),
            new StatFactor("Эффективность исследователей, %", c.Efficiency * 100, "number0"));
        Set("technologies_count", c.Researched.Count);
        Set("technology_level_pct", known > 0 ? c.Researched.Count * 100.0 / known : 0);
        Set("brain_drain_pct", c.Drain * 100);
    }
}
