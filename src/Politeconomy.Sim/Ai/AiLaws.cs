using Politeconomy.Sim.Data;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Ai;

/// <summary>
/// Законы ИИ: не чаще раза в LawMonths и только когда своих законопроектов нет, правительство перебирает все
/// варианты всех законов. Польза варианта = Σ приоритет × оценка (рост, стабильность, сила, переизбрание по эффектам
/// закона — разнице с нынешним вариантом) + одобрение избирателей правящей партии. Вносит лучший вариант, если польза
/// больше LawThreshold и парламент, по прогнозу без случайностей, его примет. MODEL.md, «ИИ стран».
/// </summary>
public static class AiLaws
{
    public static void Consider(AiTurn turn)
    {
        var world = turn.World;
        var b = turn.Balance;
        if (world.Laws?.Countries.GetValueOrDefault(turn.Id) is not { } laws || laws.Bills.Count > 0
            || !turn.Ai.Ready("law", world.Date, b.LawMonths))
            return;
        var context = Context.Of(turn);
        var candidates = new List<(LawRecord Law, string Variant, Score Score)>();
        foreach (var law in world.Data.Laws.Laws)
        {
            var current = laws.Current[law.Id];
            foreach (var v in law.Variants)
            {
                if (v.Id == current)
                    continue;
                var score = Evaluate(turn, context, law, current, v.Id);
                if (score.Utility > b.LawThreshold)
                    candidates.Add((law, v.Id, score));
            }
        }
        foreach (var c in candidates.OrderByDescending(c => c.Score.Utility).Take(3))
        {
            if (!Passes(world, turn.Id, c.Law, laws.Current[c.Law.Id], c.Variant))
                continue;
            if (!LawActions.Propose(world, turn.Id, c.Law.Id, c.Variant))
                continue;
            turn.Done("law");
            turn.Record("law", $"Вносит закон «{c.Law.Name}: {c.Law.Variant(c.Variant).Name.ToLowerInvariant()}».", Explain(turn, context, c.Score));
            return;
        }
        // Ничего стоящего или проходного нет — следующая попытка через год, а не каждый месяц.
        turn.Ai.Last["law"] = world.Date.AddMonths(12 - b.LawMonths);
    }

    /// <summary>Что о стране нужно для оценки законов — считается раз за ход.</summary>
    public sealed record Context(AiModel.Situation Situation, IReadOnlyList<double> Influence, IReadOnlyList<double>? Voters, double DebtRatio, double Deficit)
    {
        public static Context Of(AiTurn turn)
        {
            var world = turn.World;
            var influence = world.Society?.Countries.GetValueOrDefault(turn.Id)?.Influence ?? new double[world.Data.Classes.Classes.Count];
            var voters = Politics.PoliticsModel.HeadParty(world, turn.Id) is { } head ? Politics.PoliticsModel.Voters(world, turn.Id, head.Id) : null;
            var e = world.Economy![turn.Id];
            return new Context(AiModel.Assess(world, turn.Country), influence, voters, e.DebtRatio, -e.BalanceRatio * 100);
        }
    }

    /// <summary>Оценка варианта: польза, оценки по приоритетам (рост, стабильность, сила, переизбрание), одобрение избирателей правящей партии и общества.</summary>
    public sealed record Score(double Utility, double[] Scores, double Party, double Public);

    /// <summary>Польза перехода с варианта from на to.</summary>
    public static Score Evaluate(AiTurn turn, Context context, LawRecord law, string from, string to)
    {
        var data = turn.World.Data;
        var country = turn.Country;
        var publicSupport = Legislation.Support(data, law, from, to, context.Influence);
        var party = context.Voters != null ? Legislation.PartySupport(data, law, from, to, context.Voters) : publicSupport;
        var diff = Diff(law, from, to);
        double E(string key) => diff.GetValueOrDefault(key);
        double Sum(string prefix)
        {
            var sum = 0.0;
            foreach (var (key, value) in diff)
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    sum += value;
            return sum;
        }

        var s = context.Situation;
        var b = turn.Balance;
        var inflation = s.InflationCrisis(b) ? 3 : 1;
        var unrest = s.Unrest ? 2 : 1;
        // Доходы бюджета ценны, только когда долг велик или дорог; расходы стоят всегда.
        var stress = (s.DebtCrisis(b) ? 2 : 0) + 2 * Math.Max(0, context.DebtRatio - 0.6) + (context.Deficit > 3 ? 0.5 : 0);
        var growth = 2 * E("potential_growth") - 0.5 * E("natural_unemployment") - 0.3 * E("spread")
                     + inflation * (2 * E("credibility") - 0.05 * E("inflation_target")) - (inflation - 1) * 0.15 * E("monetary_control")
                     + E("small_business") + 0.5 * (E("tertiary") + E("vocational")) + 0.01 * E("index.rule_of_law")
                     + E("power_investment") + 3 * E("older_participation")
                     + stress * 0.5 * Sum("revenue.") - (1 + stress) * (0.3 * Sum("spending.") + 0.5 * E("pension_replacement") + 0.3 * E("benefit_replacement"));
        var stability = 0.5 * (publicSupport - 0.5) - 0.1 * E("radicalism") - unrest * E("protest_rate") - E("crime")
                        + 0.3 * E("life_expectancy") - 0.3 * E("food_inflation")
                        + 0.05 * (E("spending.social") + E("spending.health") + E("spending.education"));
        var freedom = E("index.civil_liberties") + E("index.press_freedom") + E("index.judicial_independence") + E("index.democracy");
        // Группы интересов (как они сдвинут одобрение закона): недовольство мешает править, а у недемократий
        // недовольные элиты (силовики, капитал) — это ещё и угроза переворота.
        var lobby = Politics.InterestGroupsModel.Lobby(turn.World, turn.Id, law, from, to);
        stability += 3 * lobby;
        var power = E("spending.defense") + 2 * E("disperse") + 0.2 * E("monetary_control")
                    - (country.Regime == Regime.Democracy ? 0 : 0.01 * freedom - 3 * lobby);
        var reelection = (party - 0.5) + 0.5 * (publicSupport - 0.5) + (country.Regime == Regime.Democracy ? 0.005 * freedom : 0);
        double[] scores = [growth, stability, power, reelection];
        var w = turn.Ai.Priorities;
        var utility = w[0] * growth + w[1] * stability + w[2] * power + w[3] * reelection - b.LawInertia;
        return new Score(utility, scores, party, publicSupport);
    }

    /// <summary>Почему: польза, одобрение, самые весомые доводы по приоритетам, кризис.</summary>
    public static string Explain(AiTurn turn, Context context, Score score)
    {
        var w = turn.Ai.Priorities;
        var reasons = new List<string> { $"избиратели правящей партии «за» на {Fmt.Number(score.Party * 100, 0)}%, общество — на {Fmt.Number(score.Public * 100, 0)}%" };
        reasons.AddRange(score.Scores.Select((x, k) => (x, k)).Where(t => Math.Abs(t.x) >= 0.05).OrderByDescending(t => Math.Abs(w[t.k] * t.x))
            .Take(3).Select(t => $"{AiModel.PriorityNames[t.k]} {Fmt.Signed(t.x, 2)}"));
        if (context.Situation.Any(turn.Balance))
            reasons.Add("кризис: " + string.Join(", ", AiModel.Crises(context.Situation, turn.Balance)));
        return $"польза {Fmt.Number(score.Utility, 2)}: " + string.Join("; ", reasons) + $" (приоритеты: {AiModel.Describe(turn.Ai)})";
    }

    /// <summary>Разница эффектов вариантов — данные не меняются, поэтому считается один раз на пару вариантов.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LawRecord, Dictionary<(string, string), Dictionary<string, double>>> Diffs = new();

    static Dictionary<string, double> Diff(LawRecord law, string from, string to)
    {
        var cache = Diffs.GetOrCreateValue(law);
        lock (cache)
        {
            if (!cache.TryGetValue((from, to), out var diff))
                cache[(from, to)] = diff = Diff(law.Variant(from).Effects, law.Variant(to).Effects);
            return diff;
        }
    }

    static Dictionary<string, double> Diff(IReadOnlyDictionary<string, double> from, IReadOnlyDictionary<string, double> to)
    {
        var diff = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var key in from.Keys.Union(to.Keys))
            diff[key] = to.GetValueOrDefault(key) - from.GetValueOrDefault(key);
        return diff;
    }

    /// <summary>Примет ли парламент закон: прогноз голосования фракций без случайного разброса (как на экране законов).</summary>
    public static bool Passes(World world, string id, LawRecord law, string from, string to)
    {
        var data = world.Data;
        if (world.Government?.Countries.GetValueOrDefault(id)?.Parliament is not { } parliament)
            return true;
        var influence = world.Society?.Countries.GetValueOrDefault(id)?.Influence ?? new double[data.Classes.Classes.Count];
        var general = Legislation.Support(data, law, from, to, influence);
        var lobby = Politics.InterestGroupsModel.Lobby(world, id, law, from, to);
        var factions = parliament.Factions.Select(f => new FactionSupport(f.Party, f.Seats, f.Ruling,
            Math.Clamp((Politics.PoliticsModel.Voters(world, id, f.Party) is { } voters ? Legislation.PartySupport(data, law, from, to, voters) : general) + lobby, 0, 1)))
            .ToList();
        return ParliamentVote.Forecast(data.GovernmentBalance.Parliament, parliament.Kind, parliament.Seats, parliament.Discipline, factions, law.Constitutional).Passed;
    }
}
