using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Ai;

/// <summary>
/// ИИ правительств (все страны, кроме страны игрока). Раз в месяц правительство пересчитывает приоритеты (рост,
/// стабильность, сила, переизбрание) из строя, взглядов правящей партии, черт главы и обстановки, затем по очереди
/// решает: бюджет (в январе), закон, визит, договор, санкции, блок, резолюция, кредит. Каждое решение — с причинами —
/// пишется в журнал решений страны. MODEL.md, «ИИ стран».
/// </summary>
public static class AiModel
{
    public const int Growth = 0, Stability = 1, Power = 2, Reelection = 3;
    public static readonly string[] PriorityNames = ["рост", "стабильность", "сила", "переизбрание"];

    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.AiSystem()];

    public static void Setup(World world)
    {
        world.Ai = new WorldAi();
        Ensure(world);
    }

    /// <summary>Создать ИИ для стран, у которых его нет (новая партия, загрузка старого мира).</summary>
    public static void Ensure(World world)
    {
        var ai = world.Ai ??= new WorldAi();
        var b = world.Data.AiBalance;
        for (var i = 0; i < world.Countries.Count; i++)
        {
            var country = world.Countries[i];
            if (ai.Countries.ContainsKey(country.Id))
                continue;
            var c = new CountryAi();
            // Сроки решений разнесены по странам: иначе в первый месяц и потом каждый срок все 198 стран думали бы разом.
            foreach (var (kind, months) in new[] { ("law", b.LawMonths), ("visit", b.VisitMonths), ("treaty", b.TreatyMonths),
                         ("sanction", b.SanctionMonths), ("bloc", b.BlocMonths), ("council", b.CouncilMonths), ("war", world.Data.WarBalance.WarMonths) })
                c.Last[kind] = world.Date.AddMonths(-months + 1 + (i * 7 + kind.Length) % months);
            ai.Countries[country.Id] = c;
            UpdatePriorities(world, country, c);
        }
    }

    /// <summary>Ход месяца: все страны без игрока по очереди, потом один пересчёт дипломатии.</summary>
    public static void Month(World world)
    {
        Ensure(world);
        foreach (var country in world.Countries)
        {
            if (country.Id == world.PlayerCountry)
                continue;
            var ai = world.Ai![country.Id];
            UpdatePriorities(world, country, ai);
            var turn = new AiTurn(world, country, ai);
            DiplomacyModel.BeginTurn(world, country.Id, text => turn.Record("diplomacy", text));
            try
            {
                if (world.Date.Month == 1)
                    AiBudget.Plan(turn);
                AiLaws.Consider(turn);
                AiDiplomacy.Consider(turn);
                AiWar.Consider(turn);
            }
            finally
            {
                DiplomacyModel.EndTurn(world);
            }
        }
        DiplomacyModel.Flush(world);
    }

    /// <summary>Что происходит в стране: инфляция, безработица, долговое бремя, массовые протесты, скорые выборы.</summary>
    public sealed record Situation(double Inflation, double Unemployment, double InterestBurden, bool Unrest, bool ElectionSoon)
    {
        public bool InflationCrisis(Data.AiBalance b) => Inflation > b.InflationCrisis;
        public bool JobsCrisis(Data.AiBalance b) => Unemployment > b.UnemploymentCrisis;
        public bool DebtCrisis(Data.AiBalance b) => InterestBurden > b.InterestCrisis;
        public bool Any(Data.AiBalance b) => InflationCrisis(b) || JobsCrisis(b) || DebtCrisis(b) || Unrest;
    }

    public static Situation Assess(World world, Country country)
    {
        var b = world.Data.AiBalance;
        var e = world.Economy![country.Id];
        var p = world.Politics?.Countries.GetValueOrDefault(country.Id);
        var next = p == null ? null : Politics.CampaignModel.Next(p);
        var soon = country.Regime != Regime.Authoritarian && next is { } date && date <= world.Date.AddMonths(b.ElectionMonths);
        var inflation = country.Stat("inflation_pct");
        var unemployment = country.Stat("unemployment_pct");
        return new Situation(double.IsFinite(inflation) ? inflation : 0, double.IsFinite(unemployment) ? unemployment : 0,
            e.TotalRevenue > 0 ? e.Interest / e.TotalRevenue : 0, p?.Protests.Any(x => x.Stage >= 2) == true, soon);
    }

    /// <summary>
    /// Приоритеты: у каждого PriorityBase; строй (демократия — переизбрание, диктатура — сила и стабильность),
    /// экономические взгляды правящей партии (правее — рост, левее — стабильность), черты главы, кризис
    /// (стабильность), скорые выборы (переизбрание). Потом — доли.
    /// </summary>
    public static void UpdatePriorities(World world, Country country, CountryAi ai)
    {
        var b = world.Data.AiBalance;
        var w = new[] { b.PriorityBase, b.PriorityBase, b.PriorityBase, b.PriorityBase };
        var why = new List<string>();
        switch (country.Regime)
        {
            case Regime.Democracy:
                w[Reelection] += b.RegimeReelection;
                why.Add("демократия: важно переизбраться");
                break;
            case Regime.Hybrid:
                w[Reelection] += b.RegimeReelection / 2;
                w[Power] += b.RegimePower / 2;
                why.Add("гибридный режим: и выборы, и контроль");
                break;
            default:
                w[Power] += b.RegimePower;
                w[Stability] += b.RegimeStability;
                why.Add("диктатура: важны сила и порядок");
                break;
        }
        if (Politics.PoliticsModel.HeadParty(world, country.Id) is { } party)
        {
            var economic = party.Position[0] / 100;
            if (economic > 0)
                w[Growth] += b.PartyEconomy * economic;
            else
                w[Stability] -= b.PartyEconomy * economic;
            if (Math.Abs(economic) > 0.2)
                why.Add(economic > 0 ? $"правящая «{party.Name}» за рынок: рост" : $"правящая «{party.Name}» за государство: стабильность");
        }
        if (world.Government?.Countries.GetValueOrDefault(country.Id)?.Leader is { } leader)
        {
            void Trait(string trait, int priority, string text)
            {
                if (!leader.Has(trait))
                    return;
                w[priority] += b.Trait;
                why.Add(text);
            }
            Trait("technocrat", Growth, "глава — технократ: рост");
            Trait("reformer", Growth, "глава — реформатор: рост");
            Trait("silovik", Power, "глава — силовик: сила");
            Trait("populist", Reelection, "глава — популист: переизбрание");
            Trait("charismatic", Reelection, "глава — харизматик: переизбрание");
        }
        var s = Assess(world, country);
        if (s.Any(b))
        {
            w[Stability] += b.Crisis;
            why.Add("кризис: " + string.Join(", ", Crises(s, b)));
        }
        if (s.ElectionSoon)
        {
            w[Reelection] += b.Election;
            why.Add("скоро выборы");
        }
        var sum = w.Sum();
        ai.Priorities = w.Select(x => x / sum).ToArray();
        ai.Why = why;
    }

    public static IEnumerable<string> Crises(Situation s, Data.AiBalance b)
    {
        if (s.InflationCrisis(b))
            yield return $"инфляция {Fmt.Number(s.Inflation, 0)}%";
        if (s.JobsCrisis(b))
            yield return $"безработица {Fmt.Number(s.Unemployment, 0)}%";
        if (s.DebtCrisis(b))
            yield return $"проценты по долгу съедают {Fmt.Number(s.InterestBurden * 100, 0)}% доходов";
        if (s.Unrest)
            yield return "массовые протесты";
    }

    /// <summary>Приоритеты одной строкой: «рост 34%, стабильность 22%, …».</summary>
    public static string Describe(CountryAi ai) =>
        string.Join(", ", ai.Priorities.Select((w, k) => (w, k)).OrderByDescending(x => x.w)
            .Select(x => $"{PriorityNames[x.k]} {Fmt.Number(x.w * 100, 0)}%"));
}

/// <summary>Ход ИИ одной страны: мир, страна, её ИИ и запись решений; Why — причина действия, которое сейчас выполняется.</summary>
public sealed class AiTurn(World world, Country country, CountryAi ai)
{
    public World World { get; } = world;
    public Country Country { get; } = country;
    public CountryAi Ai { get; } = ai;
    public string Id => Country.Id;
    public Data.AiBalance Balance => World.Data.AiBalance;
    public string Why { get; set; } = "";

    public void Record(string kind, string text, string? why = null)
    {
        Ai.Decisions.Add(new AiDecision(World.Date, kind, text, why ?? Why));
        var counts = World.Ai!.Counts;
        counts[kind] = counts.GetValueOrDefault(kind) + 1;
        var limit = Balance.Journal;
        if (Ai.Decisions.Count > limit)
            Ai.Decisions.RemoveRange(0, Ai.Decisions.Count - limit);
    }

    public void Done(string kind) => Ai.Last[kind] = World.Date;
}
