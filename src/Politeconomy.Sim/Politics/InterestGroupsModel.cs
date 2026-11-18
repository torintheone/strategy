using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Группы интересов (data/interest_groups.json): влияние — от силы их классов, доли их отраслей в выпуске, режима и
/// религиозности страны; одобрение власти — от настроения их классов, законов, бюджета и близости правительства по
/// взглядам; 2–3 требования. Недовольные давят на власть (поправки к модели), все лоббируют законы и финансируют
/// партии. MODEL.md, «Группы интересов».
/// </summary>
public static class InterestGroupsModel
{
    /// <summary>Группы во всех странах: после законов (нужны стартовые варианты) и бюджетов.</summary>
    public static void Setup(World world)
    {
        foreach (var country in world.Countries)
        {
            var p = world.Politics![country.Id];
            p.Groups = world.Data.InterestGroups.Groups.Select(g => new InterestGroup { Id = g.Id }).ToList();
            p.StartSpending = world.Economy![country.Id].SpendingPlan.ToArray();
            Update(world, country, p, months: 0);
            RefreshDemands(world, country, p);
        }
    }

    /// <summary>
    /// Месяц: влияние, взгляды, одобрение (при months = 0 сразу в цель), требования, давление и деньги партиям.
    /// </summary>
    public static void Update(World world, Country country, CountryPolitics p, int months)
    {
        var data = world.Data;
        var b = data.GroupsBalance;
        var records = data.InterestGroups.Groups;
        var s = world.Society![country.Id];
        var laws = world.Laws?.Countries.GetValueOrDefault(country.Id);
        if (months > 0)
            CheckDemands(world, country, p);
        var influence = Influence(world, country, s);
        var player = world.PlayerCountry == country.Id;
        for (var i = 0; i < records.Count && i < p.Groups.Count; i++)
        {
            var r = records[i];
            var g = p.Groups[i];
            var weights = ClassWeights(data, r);
            g.Influence = influence[i];
            for (var a = 0; a < PoliticalViews.Axes; a++)
            {
                var view = 0.0;
                for (var k = 0; k < weights.Length && k < p.Views.Length; k++)
                    view += weights[k] * p.Views[k][a];
                g.Position[a] = Math.Clamp(view + (a < r.Lean.Count ? r.Lean[a] : 0), -100, 100);
            }
            var satisfaction = 0.0;
            for (var k = 0; k < weights.Length; k++)
                satisfaction += weights[k] * s.Satisfaction[k];
            g.FromClasses = b.ClassWeight * (satisfaction - 50);
            g.FromLaws = laws == null ? 0
                : Math.Clamp(b.LawWeight * data.Laws.Laws.Sum(l => r.LawAttitude(l.Id, laws.Current[l.Id]) - r.LawAttitude(l.Id, laws.Start[l.Id])), -b.LawCap, b.LawCap);
            g.FromBudget = Budget(world, country, p, r, b);
            g.FromGovernment = Align(world, p, g, b);
            g.FromRepression = r.Repression * p.Security.Repression;
            if (months > 0)
                g.Memory *= b.DemandMemory;
            var target = Math.Clamp(50 + g.FromClasses + g.FromLaws + g.FromBudget + g.FromGovernment + g.FromRepression + g.Memory, 0, 100);
            var before = g.Approval;
            g.Approval = months == 0 ? target : g.Approval + (target - g.Approval) / b.ApprovalMonths;
            if (player && months > 0 && before >= b.Angry && g.Approval < b.Angry)
                world.Log(country.Id, "group", $"{r.Name} недовольны властью и начинают давить: {r.Pressure}.");
        }
        Effects(data, p);
        Backing(data, p);
        if (months > 0 && world.Date.Month == 1)
            RefreshDemands(world, country, p);
        p.Version++;
    }

    /// <summary>
    /// Влияние групп (доли, сумма 1): Σ доля класса, которую группа представляет × влияние класса × Clout; крупный
    /// капитал (его влияние × CapitalClout) делится между группами с отраслями пропорционально выпуску этих отраслей ×
    /// Clout; дальше × режим (× Military
    /// при военном режиме, × Monarchy при абсолютной монархии), у религиозных — × квадрат религиозности страны.
    /// </summary>
    public static double[] Influence(World world, Country country, CountrySociety s)
    {
        var data = world.Data;
        var records = data.InterestGroups.Groups;
        var classes = data.Classes.Classes;
        var industries = data.Industries.Industries;
        var e = world.Economy?.Countries.GetValueOrDefault(country.Id);
        var output = e?.Output is { Length: > 0 } o ? o.Sum() : 0;
        var sector = records.Select(r =>
        {
            if (r.Industries is not { Count: > 0 } list || e == null || output <= 0)
                return 0;
            var share = 0.0;
            for (var i = 0; i < industries.Count && i < e.Output.Length; i++)
                if (list.Contains(industries[i].Id))
                    share += e.Output[i] / output;
            return share * r.Clout;
        }).ToArray();
        var sectorTotal = sector.Sum();
        var religiosity = Religiosity(data, country);
        var regime = (int)country.Regime;
        var raw = new double[records.Count];
        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i];
            var v = 0.0;
            for (var k = 0; k < classes.Count; k++)
                v += r.Classes.GetValueOrDefault(classes[k].Id) * s.Influence[k];
            if (r.Industries is not { Count: > 0 })
                v *= r.Clout;
            else if (sectorTotal > 0)
                v += data.GroupsBalance.CapitalClout * s.Influence[ClassIds.Capitalists] * sector[i] / sectorTotal;
            v *= r.Regime[Math.Clamp(regime, 0, r.Regime.Count - 1)];
            if (country.System == PoliticalSystem.MilitaryRegime)
                v *= r.Military;
            if (country.System == PoliticalSystem.AbsoluteMonarchy)
                v *= r.Monarchy;
            // Квадрат: в набожной стране религиозные организации — одна из главных сил, в светской их почти не слышно.
            if (r.Religious)
                v *= religiosity * religiosity;
            raw[i] = v;
        }
        var total = raw.Sum();
        return raw.Select(v => total > 0 ? v / total : 1.0 / raw.Length).ToArray();
    }

    /// <summary>Религиозность страны 0–1: своя, иначе по субрегиону, иначе по умолчанию.</summary>
    public static double Religiosity(GameData data, Country country)
    {
        var r = data.InterestGroups.Religiosity;
        return r.Countries.TryGetValue(country.Id, out var own) ? own
            : r.Subregions.TryGetValue(country.Info.Subregion, out var sub) ? sub
            : r.Default;
    }

    /// <summary>
    /// Чьё настроение и отношение к законам несёт группа: доли её классов (сумма 1). Группе с отраслями достаётся
    /// и крупный капитал — целиком, если своих классов нет, иначе с весом CapitalWeight.
    /// </summary>
    public static double[] ClassWeights(GameData data, InterestGroupRecord r)
    {
        var classes = data.Classes.Classes;
        var w = classes.Select(c => r.Classes.GetValueOrDefault(c.Id)).ToArray();
        if (r.Industries is { Count: > 0 })
            w[ClassIds.Capitalists] += r.Classes.Count == 0 ? 1 : data.GroupsBalance.CapitalWeight;
        var total = w.Sum();
        return total > 0 ? w.Select(x => x / total).ToArray() : w;
    }

    /// <summary>Отношение группы к варианту закона: отношение её классов (по долям) плюс её собственное.</summary>
    public static double Attitude(GameData data, InterestGroupRecord r, IReadOnlyList<double> weights, LawRecord law, string variant)
    {
        var v = law.Variant(variant);
        var classes = data.Classes.Classes;
        var value = r.LawAttitude(law.Id, variant);
        for (var k = 0; k < classes.Count && k < weights.Count; k++)
            value += weights[k] * v.Attitudes.GetValueOrDefault(classes[k].Id);
        return value;
    }

    /// <summary>Насколько группе лучше (больше нуля) или хуже от смены варианта закона from → to.</summary>
    public static double Stance(GameData data, InterestGroupRecord r, LawRecord law, string from, string to)
    {
        var w = ClassWeights(data, r);
        return Attitude(data, r, w, law, to) - Attitude(data, r, w, law, from);
    }

    /// <summary>
    /// Лоббирование законопроекта: сдвиг одобрения в парламенте = LobbyWeight × Σ влияние группы ×
    /// th(изменение её отношения / LobbyScale). Groups — id и влияние групп страны.
    /// </summary>
    public static double Lobby(GameData data, IEnumerable<(string Id, double Influence)> groups, LawRecord law, string from, string to)
    {
        var b = data.GroupsBalance;
        var shift = 0.0;
        foreach (var (id, influence) in groups)
            if (data.InterestGroups.Groups.FirstOrDefault(g => g.Id == id) is { } r)
                shift += influence * Math.Tanh(Stance(data, r, law, from, to) / b.LobbyScale);
        return b.LobbyWeight * shift;
    }

    public static double Lobby(World world, string countryId, LawRecord law, string from, string to) =>
        world.Politics?.Countries.GetValueOrDefault(countryId) is { Groups.Count: > 0 } p
            ? Lobby(world.Data, p.Groups.Select(g => (g.Id, g.Influence)), law, from, to)
            : 0;

    /// <summary>
    /// Бюджет: BudgetWeight × средний логарифм роста расходов на статьи группы с начала игры (в пределах ±BudgetCap);
    /// у группы с Deficit — минус Deficit × дефицит сверх DeficitTolerance (не больше DeficitCap).
    /// </summary>
    static double Budget(World world, Country country, CountryPolitics p, InterestGroupRecord r, InterestGroupsBalance b)
    {
        var e = world.Economy![country.Id];
        var items = world.Data.Budget.Spending;
        var sum = 0.0;
        var count = 0;
        foreach (var id in r.Spending)
        {
            var k = Index(items, id);
            if (k < 0 || k >= p.StartSpending.Length || k >= e.SpendingPlan.Length || p.StartSpending[k] <= 0 || e.SpendingPlan[k] <= 0)
                continue;
            sum += Math.Clamp(Math.Log(e.SpendingPlan[k] / p.StartSpending[k]), -b.BudgetCap, b.BudgetCap);
            count++;
        }
        var value = count > 0 ? b.BudgetWeight * sum / count : 0;
        if (r.Deficit > 0 && country.Stat("budget_balance_pct_gdp") is var balance && double.IsFinite(balance))
            value -= Math.Min(b.DeficitCap, r.Deficit * Math.Max(0, -balance - b.DeficitTolerance));
        return value;
    }

    static int Index(IReadOnlyList<SpendingRecord> items, string id)
    {
        for (var k = 0; k < items.Count; k++)
            if (items[k].Id == id)
                return k;
        return -1;
    }

    /// <summary>
    /// Правительство: AlignWeight × (среднее расстояние от группы до партий по опросам − расстояние до правящих).
    /// Правящие ближе обычного — плюс, дальше — минус. Без партий — 0.
    /// </summary>
    static double Align(World world, CountryPolitics p, InterestGroup g, InterestGroupsBalance b)
    {
        var ruling = p.Parties.Where(x => x.Ruling).ToList();
        if (p.Parties.Count < 2 || ruling.Count == 0)
            return 0;
        var salience = world.Data.PoliticsBalance.Salience;
        double Mean(IReadOnlyList<Party> list)
        {
            var weight = list.Sum(x => Math.Max(0.01, x.Poll));
            return list.Sum(x => Math.Max(0.01, x.Poll) * PoliticalViews.Distance(g.Position, x.Position, salience)) / weight;
        }
        return b.AlignWeight * (Mean(p.Parties) - Mean(ruling));
    }

    /// <summary>
    /// Давление группы: 1 при одобрении 0, 0 от Angry и выше; довольная (выше Calm) — со знаком минус, до HappyShare.
    /// </summary>
    public static double Pressure(InterestGroupsBalance b, double approval) =>
        Math.Clamp((b.Angry - approval) / b.Angry, 0, 1) - b.HappyShare * Math.Clamp((approval - b.Calm) / (100 - b.Calm), 0, 1);

    /// <summary>Сила давления группы: доля влияния × число групп (средняя группа — 1), не больше MaxWeight.</summary>
    public static double Weight(InterestGroupsBalance b, double influence, int groups) => Math.Min(b.MaxWeight, influence * groups);

    /// <summary>Поправки к модели от групп: эффект группы × сила × давление, по всем группам.</summary>
    static void Effects(GameData data, CountryPolitics p)
    {
        var b = data.GroupsBalance;
        var records = data.InterestGroups.Groups;
        p.GroupEffects.Clear();
        for (var i = 0; i < records.Count && i < p.Groups.Count; i++)
        {
            var g = p.Groups[i];
            var pressure = Pressure(b, g.Approval) * Weight(b, g.Influence, p.Groups.Count);
            if (pressure == 0)
                continue;
            foreach (var (key, value) in records[i].Effects)
                p.GroupEffects[key] = p.GroupEffects.GetValueOrDefault(key) + value * pressure;
        }
        foreach (var key in p.GroupEffects.Where(kv => Math.Abs(kv.Value) < 1e-6).Select(kv => kv.Key).ToList())
            p.GroupEffects.Remove(key);
        {
        }
    }

    /// <summary>
    /// Деньги на выборы: довольная группа (выше FundHappy) поддерживает правящие партии, недовольная (ниже FundAngry) —
    /// ближайшую к ней оппозиционную. Прибавка = FundWeight × влияние × насколько группа довольна или зла.
    /// </summary>
    static void Backing(GameData data, CountryPolitics p)
    {
        var b = data.GroupsBalance;
        foreach (var party in p.Parties)
            party.Backing = 0;
        if (p.Parties.Count == 0)
            return;
        var salience = data.PoliticsBalance.Salience;
        foreach (var g in p.Groups)
        {
            var money = b.FundWeight * g.Influence;
            if (g.Approval > b.FundHappy)
            {
                foreach (var party in p.Parties.Where(x => x.Ruling))
                    party.Backing += money * (g.Approval - b.FundHappy) / (100 - b.FundHappy);
            }
            else if (g.Approval < b.FundAngry
                     && p.Parties.Where(x => !x.Ruling).MinBy(x => PoliticalViews.Distance(g.Position, x.Position, salience)) is { } closest)
                closest.Backing += money * (b.FundAngry - g.Approval) / b.FundAngry;
        }
    }

    /// <summary>
    /// Новые требования (на старте и каждый январь), до LawDemands законов и одного требования по бюджету: законы, вариант
    /// которых лучше нынешнего для группы хотя бы на DemandThreshold (самые желанные); рост расходов на статью группы,
    /// которая выросла меньше всех, на SpendingDemand; у финансистов при большом дефиците — сократить его.
    /// </summary>
    public static void RefreshDemands(World world, Country country, CountryPolitics p)
    {
        var data = world.Data;
        var b = data.GroupsBalance;
        var records = data.InterestGroups.Groups;
        var laws = world.Laws?.Countries.GetValueOrDefault(country.Id);
        var e = world.Economy![country.Id];
        var items = data.Budget.Spending;
        for (var i = 0; i < records.Count && i < p.Groups.Count; i++)
        {
            var r = records[i];
            var g = p.Groups[i];
            var weights = ClassWeights(data, r);
            var missing = b.LawDemands - g.Demands.Count(d => d.Kind == "law");
            if (laws != null && missing > 0)
            {
                var wanted = data.Laws.Laws
                    .Where(l => g.Demands.All(d => d.Target != l.Id) && laws.Bills.All(x => x.Law != l.Id))
                    .Select(l =>
                    {
                        var now = Attitude(data, r, weights, l, laws.Current[l.Id]);
                        var best = l.Variants.Where(v => v.Id != laws.Current[l.Id])
                            .Select(v => (Variant: v.Id, Gain: Attitude(data, r, weights, l, v.Id) - now))
                            .OrderByDescending(x => x.Gain).ThenBy(x => x.Variant, StringComparer.Ordinal).FirstOrDefault();
                        return (Law: l.Id, best.Variant, best.Gain);
                    })
                    .Where(x => x.Variant != null && x.Gain >= b.DemandThreshold)
                    .OrderByDescending(x => x.Gain).ThenBy(x => x.Law, StringComparer.Ordinal)
                    .Take(missing);
                foreach (var (law, variant, _) in wanted)
                    g.Demands.Add(new GroupDemand { Kind = "law", Target = law, Variant = variant, Since = world.Date });
            }
            if (g.Demands.Any(d => d.Kind is "spending" or "deficit"))
                continue;
            var balance = country.Stat("budget_balance_pct_gdp");
            if (r.Deficit > 0 && double.IsFinite(balance) && -balance > b.DeficitTolerance)
            {
                g.Demands.Add(new GroupDemand { Kind = "deficit", Target = "deficit", Level = b.DeficitTolerance, Since = world.Date });
                continue;
            }
            var item = r.Spending.Select(id => Index(items, id))
                .Where(k => k >= 0 && k < e.SpendingPlan.Length && k < p.StartSpending.Length && p.StartSpending[k] > 0 && e.SpendingPlan[k] > 0)
                .OrderBy(k => e.SpendingPlan[k] / p.StartSpending[k]).ThenBy(k => k)
                .Cast<int?>().FirstOrDefault();
            if (item is { } k2)
                g.Demands.Add(new GroupDemand { Kind = "spending", Target = items[k2].Id, Level = e.SpendingPlan[k2] * (1 + b.SpendingDemand), Since = world.Date });
        }
    }

    /// <summary>
    /// Выполнено ли требование: закон принят, расходы подняты до нужного уровня, дефицит сокращён. Выполненное
    /// снимается и прибавляет группе DemandBonus к одобрению; требование закона, которое потеряло смысл (закон
    /// сменили на другой подходящий вариант), снимается молча.
    /// </summary>
    static void CheckDemands(World world, Country country, CountryPolitics p)
    {
        var data = world.Data;
        var b = data.GroupsBalance;
        var records = data.InterestGroups.Groups;
        var laws = world.Laws?.Countries.GetValueOrDefault(country.Id);
        var e = world.Economy![country.Id];
        var player = world.PlayerCountry == country.Id;
        for (var i = 0; i < records.Count && i < p.Groups.Count; i++)
        {
            var r = records[i];
            var g = p.Groups[i];
            foreach (var d in g.Demands.ToList())
            {
                if (Done(world, country, d, laws, e))
                {
                    g.Demands.Remove(d);
                    g.Memory += b.DemandBonus;
                    if (player)
                        world.Log(country.Id, "group", $"{r.Name} довольны: выполнено их требование — {Describe(data, d)}.");
                }
                else if (d.Kind == "law" && laws != null && d.Variant != null)
                {
                    var law = data.Law(d.Target);
                    if (Stance(data, r, law, laws.Current[d.Target], d.Variant) < b.DemandThreshold)
                        g.Demands.Remove(d);
                }
            }
        }
    }

    public static bool Done(World world, Country country, GroupDemand d, Laws.CountryLaws? laws, Economy.CountryEconomy e) => d.Kind switch
    {
        "law" => laws != null && laws.Current.GetValueOrDefault(d.Target) == d.Variant,
        "spending" => Index(world.Data.Budget.Spending, d.Target) is var k && k >= 0 && k < e.SpendingPlan.Length && e.SpendingPlan[k] >= d.Level * (1 - 1e-9),
        "deficit" => country.Stat("budget_balance_pct_gdp") is var balance && double.IsFinite(balance) && -balance <= d.Level,
        _ => false,
    };

    /// <summary>Требование словами: «закон …», «расходы на … — до …% ВВП», «дефицит бюджета — не больше 3% ВВП».</summary>
    public static string Describe(GameData data, GroupDemand d)
    {
        switch (d.Kind)
        {
            case "law":
                var law = data.Law(d.Target);
                return $"закон «{law.Name}: {law.Variant(d.Variant!).Name.ToLowerInvariant()}»";
            case "spending":
                var item = data.Budget.Spending.FirstOrDefault(s => s.Id == d.Target);
                return $"расходы на «{item?.Name ?? d.Target}» — до {Text.Fmt.Number(d.Level * 100, 2)}% ВВП";
            default:
                return $"дефицит бюджета — не больше {Text.Fmt.Number(d.Level, 0)}% ВВП";
        }
    }
}
