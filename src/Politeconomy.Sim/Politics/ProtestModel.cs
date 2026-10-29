using Politeconomy.Sim.Data;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Протесты классов и ответ власти, лояльность силовиков, репрессии и аресты оппозиции. Протест начинается тем вероятнее,
/// чем выше радикализм и организованность класса; поводом бывает и ненавистный закон, и фальсификации. Каждый месяц он
/// разгорается или затухает; власть может уступить, договориться, промолчать или разогнать. MODEL.md, «Протесты».
/// </summary>
public static class ProtestModel
{
    public static readonly string[] Stages = ["забастовка", "митинги", "массовые протесты", "беспорядки", "восстание"];
    public static readonly string[] Responses = ["concede", "negotiate", "ignore", "disperse"];

    /// <summary>Месяц: лояльность силовиков, новые протесты, эскалация, ответ компьютера, поправки к модели.</summary>
    public static void Update(World world, Country country, CountryPolitics p)
    {
        var data = world.Data;
        var b = data.UnrestBalance;
        var s = world.Society![country.Id];
        if (p.Concessions.Length != s.Radicalism.Length)
            p.Concessions = new double[s.Radicalism.Length];
        for (var k = 0; k < p.Concessions.Length; k++)
            p.Concessions[k] *= b.MoodDecay;
        var sec = p.Security;
        sec.PoliceStrain *= b.StrainDecay;
        sec.ArmyStrain *= b.StrainDecay;
        sec.Repression = sec.Repression * b.RepressionDecay < 0.1 ? 0 : sec.Repression * b.RepressionDecay;
        Loyalty(world, country, p);

        var rng = PoliticsModel.Rng(world);
        var player = world.PlayerCountry == country.Id;
        // Новые протесты: у каждого класса — не больше одного.
        for (var k = 0; k < s.Radicalism.Length; k++)
        {
            if (!CanProtest(data, k) || p.Protests.Any(x => x.Class == k))
                continue;
            if (rng.Chance(StartChance(world, country, p, s, k)))
                Start(world, country, p, k, "mood", stage: rng.Chance(b.StartRally) ? 1 : 0);
        }
        // Старые: разгораются или затухают, компьютер отвечает.
        foreach (var protest in p.Protests.ToList())
        {
            protest.Months++;
            // Новости месяца (жёсткое задержание, гибель протестующего, удачная речь) — случайный сдвиг накала.
            var heat = Heat(world, country, s, protest) + rng.NextGaussian() * b.HeatNoise;
            var cap = s.Radicalism[protest.Class] >= b.UprisingRadicalism ? Stages.Length - 1 : Stages.Length - 2;
            var up = protest.Stage >= cap ? 0 : Math.Clamp(b.Escalate * (heat - b.EscalateFrom) + LawModel.Effect(world, country.Id, "protest_escalation"), 0, b.MaxChance);
            var down = Math.Clamp(b.Calm * (b.CalmFrom - heat), 0, b.MaxChance);
            var roll = rng.NextDouble();
            if (roll < up)
            {
                protest.Stage++;
                if (player || protest.Stage >= 2)
                    world.Log(country.Id, "protest", $"{Title(data, protest)}: протест разгорается — {StageName(data, protest)}.");
            }
            else if (roll < up + down)
            {
                protest.Stage--;
                if (protest.Stage < 0)
                {
                    p.Protests.Remove(protest);
                    if (player)
                        world.Log(country.Id, "protest", $"{ClassName(data, protest.Class)}: протест утих.");
                    continue;
                }
            }
            protest.Heat *= b.HeatDecay;
            protest.Anger *= b.HeatDecay;
            protest.Calm *= b.HeatDecay;
            if (!player && protest.Answered != world.Date)
                Respond(world, country, p, protest, AiResponse(world, country, p, protest));
        }
        Effects(world, country, p);
        Strike(world, country, p);
        p.Version++;
    }

    /// <summary>Старт: лояльность силовиков и индекс беспорядков (протестов ещё нет).</summary>
    public static void Setup(World world)
    {
        foreach (var country in world.Countries)
        {
            var p = world.Politics![country.Id];
            p.Concessions = new double[world.Data.Classes.Classes.Count];
            Loyalty(world, country, p);
            Effects(world, country, p);
        }
    }

    /// <summary>Капиталисты и госаппарат на улицы не выходят: их недовольство идёт через группы интересов и элиты.</summary>
    public static bool CanProtest(GameData data, int k) => k < data.Classes.Classes.Count && !data.UnrestBalance.Quiet.Contains(data.Classes.Classes[k].Id);

    /// <summary>
    /// Давление класса: ((радикализм − Floor) / Scale + злость от низкой удовлетворённости, от 0 до 2) × (OrganizationBase + организованность) ×
    /// (1 + склонность к протестам по законам), не меньше 0.
    /// </summary>
    public static double Pressure(World world, Country country, CountrySociety s, int k)
    {
        var b = world.Data.UnrestBalance;
        var radical = Math.Clamp(Math.Max(0, (s.Radicalism[k] - b.Floor) / b.Scale) + Math.Max(0, (b.Discontent - s.Satisfaction[k]) / b.DiscontentScale), 0, 2);
        return Math.Max(0, radical * (b.OrganizationBase + s.Organization[k]) * (1 + LawModel.Effect(world, country.Id, "protest_rate")));
    }

    /// <summary>Вероятность, что класс выйдет на улицы в этом месяце: StartRate × давление, у маленьких классов меньше.</summary>
    public static double StartChance(World world, Country country, CountryPolitics p, CountrySociety s, int k)
    {
        var b = world.Data.UnrestBalance;
        return b.StartRate * Pressure(world, country, s, k) * Math.Min(1, AdultShare(s, k) / b.SmallClass);
    }

    /// <summary>Накал протеста: давление класса + повод + гнев − успокоение − усталость.</summary>
    static double Heat(World world, Country country, CountrySociety s, Protest protest) =>
        Pressure(world, country, s, protest.Class) + protest.Heat + protest.Anger - protest.Calm - world.Data.UnrestBalance.Fatigue * protest.Months;

    static double AdultShare(CountrySociety s, int k)
    {
        var total = s.Heads.Sum();
        return total > 0 ? s.Heads[k] / total : 0;
    }

    /// <summary>Масштаб протеста по численности класса: доля взрослых / SizeScale, от 0,3 до 1,5.</summary>
    public static double Size(GameData data, CountrySociety s, int k) => Math.Clamp(AdultShare(s, k) / data.UnrestBalance.SizeScale, 0.3, 1.5);

    /// <summary>Сила протеста: вес стадии × масштаб.</summary>
    public static double Severity(GameData data, CountrySociety s, Protest protest) =>
        data.UnrestBalance.Severity[Math.Clamp(protest.Stage, 0, Stages.Length - 1)] * Size(data, s, protest.Class);

    /// <summary>Сколько человек на улицах (оценка): доля взрослых класса на этой стадии.</summary>
    public static double Participants(GameData data, CountrySociety s, Protest protest) =>
        s.Heads[protest.Class] * data.UnrestBalance.Turnout[Math.Clamp(protest.Stage, 0, Stages.Length - 1)];

    /// <summary>
    /// Начать протест класса (или подогреть идущий): повод, требование (закон и прежний вариант), стадия.
    /// </summary>
    public static Protest Start(World world, Country country, CountryPolitics p, int k, string cause, int stage, string? law = null, string? variant = null)
    {
        var data = world.Data;
        var existing = p.Protests.FirstOrDefault(x => x.Class == k);
        if (existing != null)
        {
            existing.Heat += data.UnrestBalance.CauseHeat;
            existing.Stage = Math.Max(existing.Stage, stage);
            if (law != null)
                (existing.Cause, existing.Law, existing.Variant) = (cause, law, variant);
            Announce(world, country, existing);
            Effects(world, country, p);
            p.Version++;
            return existing;
        }
        var protest = new Protest
        {
            Id = p.NextProtest++,
            Class = k,
            Stage = stage,
            Cause = cause,
            Law = law,
            Variant = variant,
            Started = world.Date,
            Heat = cause == "mood" ? 0 : data.UnrestBalance.CauseHeat,
        };
        p.Protests.Add(protest);
        Announce(world, country, protest);
        Effects(world, country, p);
        p.Version++;
        return protest;
    }

    static void Announce(World world, Country country, Protest protest)
    {
        if (world.PlayerCountry == country.Id || protest.Stage >= 2)
            world.Log(country.Id, "protest", $"{Title(world.Data, protest)}: {StageName(world.Data, protest)} — {CauseText(world.Data, protest)}.");
    }

    /// <summary>Закон принят: классы, которым он сильно не нравится, выходят на митинги с требованием его отменить.</summary>
    public static void LawPassed(World world, string countryId, LawRecord law, string from, string to)
    {
        var data = world.Data;
        var b = data.UnrestBalance;
        if (world.Politics?.Countries.GetValueOrDefault(countryId) is not { } p || world.Society?.Countries.GetValueOrDefault(countryId) is not { } s)
            return;
        var shifts = Legislation.Shifts(data, law, from, to);
        for (var k = 0; k < shifts.Length; k++)
            if (CanProtest(data, k) && shifts[k] <= -b.LawShift && PoliticsModel.Rng(world).Chance(b.LawChance + b.LawOrganization * s.Organization[k]))
                Start(world, world[countryId], p, k, "law", stage: 1, law.Id, from);
    }

    /// <summary>Фальсификации раскрыты: горожане (специалисты, служащие, рабочие — кто организованнее) выходят на массовые протесты.</summary>
    public static void FraudExposed(World world, Country country, CountryPolitics p)
    {
        var s = world.Society![country.Id];
        int[] urban = [ClassIds.Professionals, ClassIds.Clerks, ClassIds.Skilled];
        var k = urban.OrderByDescending(c => s.Organization[c] * AdultShare(s, c)).First();
        Start(world, country, p, k, "fraud", stage: 2);
    }

    /// <summary>
    /// Ответ власти на протест (не чаще раза в месяц). concede — уступить: отменить закон или дать деньги классу;
    /// negotiate — переговоры (успех зависит от организованности класса); ignore — промолчать; disperse — разогнать
    /// (полицией, с беспорядков — армией; успех зависит от их лояльности). Возвращает итог словами.
    /// </summary>
    public static string? Respond(World world, Country country, CountryPolitics p, Protest protest, string response)
    {
        if (!p.Protests.Contains(protest) || protest.Answered == world.Date || !Responses.Contains(response))
            return null;
        var data = world.Data;
        var b = data.UnrestBalance;
        var s = world.Society![country.Id];
        var rng = PoliticsModel.Rng(world);
        var k = protest.Class;
        protest.Answered = world.Date;
        protest.Response = response;
        var title = Title(data, protest);
        string result;
        switch (response)
        {
            case "concede":
                if (protest.Law != null && protest.Variant != null && world.Laws?.Countries.GetValueOrDefault(country.Id) is { } laws
                    && laws.Current.GetValueOrDefault(protest.Law) is { } current && current != protest.Variant)
                {
                    var law = data.Law(protest.Law);
                    laws.Current[protest.Law] = protest.Variant;
                    laws.Transitions.RemoveAll(t => t.Law == protest.Law);
                    LawModel.Recompute(data, laws, world.Date);
                    laws.Version++;
                    p.Protests.Remove(protest);
                    result = $"власть уступила: закон «{law.Name}» возвращён к варианту «{law.Variant(protest.Variant).Name.ToLowerInvariant()}». Протест закончился";
                }
                else
                {
                    var item = Concession(world, country, k);
                    p.Concessions[k] += b.ConcessionMood;
                    protest.Calm += b.ConcessionCalm;
                    protest.Stage -= 2;
                    if (protest.Stage < 0)
                        p.Protests.Remove(protest);
                    result = $"власть уступила: больше денег на «{item}». " + (protest.Stage < 0 ? "Протест закончился" : "Протест стих");
                }
                break;
            case "negotiate":
                if (rng.Chance(NegotiateChance(b, s, protest)))
                {
                    p.Concessions[k] += b.NegotiateMood;
                    protest.Calm += b.NegotiateCalm;
                    protest.Stage--;
                    if (protest.Stage < 0)
                        p.Protests.Remove(protest);
                    result = "переговоры удались. " + (protest.Stage < 0 ? "Протест закончился" : "Протест стих");
                }
                else
                {
                    protest.Anger += 0.2;
                    result = "переговоры сорвались";
                }
                break;
            case "disperse":
                var army = protest.Stage >= b.ArmyStage;
                var sec = p.Security;
                var chance = DisperseChance(world, country, p, protest);
                sec.Repression = Math.Min(100, sec.Repression + b.DisperseRepression * (protest.Stage + 1));
                s.Radicalism[k] = Math.Min(100, s.Radicalism[k] + b.DisperseRadicalism);
                protest.Anger += b.DisperseAnger;
                if (army)
                    sec.ArmyStrain += b.DisperseStrain * (protest.Stage + 1);
                else
                    sec.PoliceStrain += b.DisperseStrain * (protest.Stage + 1);
                if (rng.Chance(chance))
                {
                    protest.Stage -= 2;
                    if (protest.Stage < 0)
                        p.Protests.Remove(protest);
                    result = (army ? "армия разогнала протест" : "полиция разогнала протест") + ". Гнев остался: радикализм растёт";
                }
                else
                {
                    if (army)
                        sec.ArmyStrain += b.FailedStrain;
                    else
                        sec.PoliceStrain += b.FailedStrain;
                    protest.Stage = Math.Min(Stages.Length - 1, protest.Stage + 1);
                    result = (army ? "армия не справилась" : "полиция не справилась") + " — протест разгорается";
                }
                Loyalty(world, country, p);
                break;
            default:
                protest.Anger += 0.1;
                result = "власть промолчала";
                break;
        }
        if (world.PlayerCountry == country.Id || protest.Stage >= 2)
            world.Log(country.Id, "protest", $"{title}: {result}.");
        Effects(world, country, p);
        p.Version++;
        return result;
    }

    /// <summary>Шанс переговоров: NegotiateBase + NegotiateOrganization × организованность класса − NegotiateStage × стадия.</summary>
    public static double NegotiateChance(UnrestBalance b, CountrySociety s, Protest protest) =>
        Math.Clamp(b.NegotiateBase + b.NegotiateOrganization * s.Organization[protest.Class] - b.NegotiateStage * protest.Stage, 0.05, 0.9);

    /// <summary>Шанс разгона: DisperseBase + DisperseLoyalty × лояльность (полиции, с беспорядков — армии) − DisperseStage × стадия + сила полиции по законам.</summary>
    public static double DisperseChance(World world, Country country, CountryPolitics p, Protest protest)
    {
        var b = world.Data.UnrestBalance;
        var loyalty = protest.Stage >= b.ArmyStage ? p.Security.ArmyLoyalty : p.Security.PoliceLoyalty;
        return Math.Clamp(b.DisperseBase + b.DisperseLoyalty * loyalty / 100 - b.DisperseStage * protest.Stage + LawModel.Effect(world, country.Id, "disperse"), 0.05, 0.95);
    }

    /// <summary>Уступка деньгами: прибавка к расходам на статью, важную классу (возвращает её название).</summary>
    static string Concession(World world, Country country, int k)
    {
        var data = world.Data;
        var b = data.UnrestBalance;
        var id = data.Classes.Classes[k].Id switch
        {
            "farmers" => "agriculture",
            "professionals" => "education",
            "clerks" => "administration",
            "small_business" => "transport",
            "skilled" => "industry",
            _ => "social",
        };
        var items = data.Budget.Spending;
        var index = Math.Max(0, items.ToList().FindIndex(x => x.Id == id));
        var e = world.Economy![country.Id];
        if (index < e.SpendingPlan.Length)
            e.SpendingPlan[index] += b.ConcessionSpending * Size(data, world.Society![country.Id], k);
        return items[index].Name;
    }

    /// <summary>
    /// Ответ компьютера: демократия договаривается (на массовых протестах против закона и на беспорядках — уступает),
    /// гибридный режим договаривается до беспорядков, а их разгоняет; авторитарный разгоняет с митингов. Разгоняют, только
    /// если шанс не ниже половины, иначе уступают. Забастовки и пикеты все пережидают.
    /// </summary>
    static string AiResponse(World world, Country country, CountryPolitics p, Protest protest)
    {
        if (protest.Stage == 0)
            return "ignore";
        // Разгонять, только если есть кому: при нелояльных силовиках — уступки.
        var force = DisperseChance(world, country, p, protest) >= 0.5;
        return country.Regime switch
        {
            Regime.Democracy => protest.Stage >= 2 && (protest.Law != null || protest.Stage >= 3) ? "concede" : "negotiate",
            Regime.Hybrid => protest.Stage >= 3 ? force ? "disperse" : "concede" : "negotiate",
            _ => force ? "disperse" : protest.Stage >= 3 ? "concede" : "negotiate",
        };
    }

    /// <summary>
    /// Лояльность полиции и армии: одобрение группы «Силовики» − нагрузка от разгонов; главе-силовику — плюс, армии при
    /// военном режиме — плюс.
    /// </summary>
    public static void Loyalty(World world, Country country, CountryPolitics p)
    {
        var b = world.Data.UnrestBalance;
        var approval = p.Groups.FirstOrDefault(g => g.Id == "security")?.Approval ?? 50;
        var leader = world.Government?.Countries.GetValueOrDefault(country.Id)?.Leader.Has("silovik") == true ? b.SilovikLeader : 0;
        var military = country.System == PoliticalSystem.MilitaryRegime ? b.MilitaryArmy : 0;
        p.Security.PoliceLoyalty = Math.Clamp(approval - p.Security.PoliceStrain + leader, 0, 100);
        p.Security.ArmyLoyalty = Math.Clamp(approval - p.Security.ArmyStrain + leader + military, 0, 100);
    }

    /// <summary>
    /// Поправки к модели: протесты бьют по выпуску (сразу по разрыву выпуска), легитимности, спреду и рейтингу правящих;
    /// репрессии — по легитимности и индексам свобод и поднимают радикализм. Публикует индекс беспорядков.
    /// </summary>
    static void Effects(World world, Country country, CountryPolitics p)
    {
        var data = world.Data;
        var b = data.UnrestBalance;
        var s = world.Society![country.Id];
        var fx = p.ProtestEffects;
        fx.Clear();
        var unrest = 0.0;
        var output = 0.0;
        foreach (var protest in p.Protests)
        {
            var stage = Math.Clamp(protest.Stage, 0, Stages.Length - 1);
            var size = Size(data, s, protest.Class);
            unrest += Severity(data, s, protest);
            output += b.Output[stage] * size;
            Add(fx, "legitimacy", -b.Legitimacy[stage] * size);
            Add(fx, "spread", b.Spread[stage] * size);
        }
        var r = p.Security.Repression;
        Add(fx, "legitimacy", -b.RepressionLegitimacy * r);
        Add(fx, "index.civil_liberties", -b.RepressionLiberties * r);
        Add(fx, "index.democracy", -b.RepressionDemocracy * r);
        Add(fx, "index.press_freedom", -b.RepressionPress * r);
        Add(fx, "radicalism", b.RepressionRadicalism * r);
        foreach (var key in fx.Where(kv => Math.Abs(kv.Value) < 1e-9).Select(kv => kv.Key).ToList())
            fx.Remove(key);
        p.UnrestOutput = output;
        p.Unrest = Math.Min(100, b.UnrestIndex * unrest);
        country.Stats["unrest_index"] = new StatValue(p.Unrest, world.Date.Year, StatSources.Model, null, null,
            p.Protests.Count == 0 ? null : p.Protests.Select(x => new StatFactor(Title(data, x), Math.Min(100, b.UnrestIndex * Severity(data, s, x)), "number0")).ToArray());
    }

    static void Add(Dictionary<string, double> fx, string key, double value) => fx[key] = fx.GetValueOrDefault(key) + value;

    /// <summary>Месяц протестов для экономики: удар по разрыву выпуска и по рейтингу правящих партий.</summary>
    static void Strike(World world, Country country, CountryPolitics p)
    {
        if (p.UnrestOutput > 0 && world.Economy?.Countries.GetValueOrDefault(country.Id) is { } e)
            e.GapCore -= p.UnrestOutput;
        if (p.Unrest > 0)
            foreach (var party in p.Parties.Where(x => x.Ruling))
                party.Momentum -= world.Data.UnrestBalance.RulingDrift * p.Unrest / world.Data.UnrestBalance.UnrestIndex;
    }

    /// <summary>
    /// Аресты оппозиции (жёсткая мера игрока): главная оппозиционная партия теряет лидера, часть организации и симпатий;
    /// репрессии растут; при свободной прессе — скандал; избиратели партии выходят на митинги. Не чаще раза в ArrestCooldown месяцев.
    /// </summary>
    public static bool Arrest(World world, string countryId)
    {
        var data = world.Data;
        var b = data.UnrestBalance;
        if (world.Politics?.Countries.GetValueOrDefault(countryId) is not { } p || !CanArrest(world, countryId) || Target(p) is not { } party)
            return false;
        var country = world[countryId];
        var old = party.Leader?.Name;
        party.Leader = PartyFactory.PartyLeader(world, country, party);
        party.Momentum -= b.ArrestMomentum;
        party.Organization *= b.ArrestOrganization;
        p.Security.Repression = Math.Min(100, p.Security.Repression + b.ArrestRepression);
        p.Security.Arrests = world.Date;
        var press = country.Stat("press_freedom_index");
        p.Scandal += b.ArrestScandal * (double.IsFinite(press) ? press : 50) / 100;
        world.Log(countryId, "repression", $"Аресты оппозиции: лидер партии «{party.Name}» {old} и её активисты задержаны. Партию возглавил {party.Leader.Name}. " +
                                           "Оппозиция ослабла, но свобод стало меньше, а недовольных — больше.");
        var voters = Enumerable.Range(0, party.Voters.Length).Where(k => CanProtest(data, k)).OrderByDescending(k => party.Voters[k]).First();
        Start(world, country, p, voters, "arrests", stage: 1);
        Effects(world, country, p);
        p.Version++;
        return true;
    }

    /// <summary>Против кого аресты: крупнейшая оппозиционная партия по опросам.</summary>
    public static Party? Target(CountryPolitics p) => p.Parties.Where(x => !x.Ruling).MaxBy(x => x.Poll);

    public static bool CanArrest(World world, string countryId) =>
        world.Politics?.Countries.GetValueOrDefault(countryId) is { } p && Target(p) != null
        && (p.Security.Arrests is not { } last || world.Date >= last.AddMonths(world.Data.UnrestBalance.ArrestCooldown));

    public static string ClassName(GameData data, int k) => data.Classes.Classes[k].Name;

    /// <summary>Заголовок протеста: «Пенсионеры», «Квалифицированные рабочие».</summary>
    public static string Title(GameData data, Protest protest) => ClassName(data, protest.Class);

    /// <summary>Название стадии для класса: у рабочих стадия 0 — забастовка, у фермеров — перекрытие дорог, у пенсионеров и безработных — пикеты.</summary>
    public static string StageName(GameData data, Protest protest) => protest.Stage == 0
        ? data.Classes.Classes[protest.Class].Id switch
        {
            "farmers" => "перекрытие дорог",
            "pensioners" or "unemployed" => "пикеты",
            "small_business" => "забастовка предпринимателей",
            _ => Stages[0],
        }
        : Stages[Math.Clamp(protest.Stage, 0, Stages.Length - 1)];

    /// <summary>Повод словами.</summary>
    public static string CauseText(GameData data, Protest protest) => protest.Cause switch
    {
        "law" when protest.Law != null => $"против закона «{data.Law(protest.Law).Name}»",
        "fraud" => "против фальсификаций на выборах",
        "arrests" => "против арестов оппозиции",
        _ => "недовольство жизнью",
    };
}
