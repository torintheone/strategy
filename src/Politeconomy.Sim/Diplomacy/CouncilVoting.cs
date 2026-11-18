using Politeconomy.Sim.Data;

namespace Politeconomy.Sim.Diplomacy;

/// <summary>Что нужно для подсчёта голосов: отношения, союзы, состав Общего собрания и Совета безопасности.</summary>
public interface IVoteView
{
    double Relations(string a, string b);
    bool Allies(string a, string b);
    IReadOnlyList<string> Assembly { get; }
    IReadOnlyList<string> Security { get; }
    IReadOnlyCollection<string> Permanent { get; }
}

/// <summary>Итог голосования: за, против, воздержались, принята ли, кто из постоянных членов наложил вето, кто голосовал «за».</summary>
public sealed record VoteCount(int Yes, int No, int Abstain, bool Passed, IReadOnlyList<string> Vetoes, IReadOnlyList<string> YesVoters, int Voters);

/// <summary>
/// Голосование в Совете Наций — одни правила для модели и для прогноза на экране. Каждый член сравнивает отношения:
/// против страны — (с автором − с целью) / 2, в её пользу — (с автором + с целью) / 2; союзники цели держатся за неё.
/// «За» — если перевес больше VoteThreshold, «против» — если меньше −VoteThreshold, иначе воздерживается.
/// Общее собрание: «за» больше, чем «против». Совет безопасности: не меньше SecurityVotes «за» и ни одного «против»
/// у постоянных членов (вето). MODEL.md, «Совет Наций».
/// </summary>
public static class CouncilVoting
{
    /// <summary>Сколько добавляет союз с целью: союзники не голосуют против своих.</summary>
    public const double Solidarity = 20;

    public static VoteCount Count(IVoteView view, DiplomacyBalance b, ResolutionRecord r, string proposer, string target)
    {
        var security = r.Chamber == "security";
        var voters = security ? view.Security : view.Assembly;
        int yes = 0, no = 0, abstain = 0;
        var vetoes = new List<string>();
        var yesVoters = new List<string>();
        foreach (var m in voters)
        {
            var vote = Vote(view, b, r, proposer, target, m);
            if (vote > 0)
            {
                yes++;
                yesVoters.Add(m);
            }
            else if (vote < 0)
            {
                no++;
                if (security && view.Permanent.Contains(m))
                    vetoes.Add(m);
            }
            else
                abstain++;
        }
        var passed = security ? yes >= b.SecurityVotes && vetoes.Count == 0 : yes > no;
        return new VoteCount(yes, no, abstain, passed, vetoes, yesVoters, voters.Count);
    }

    /// <summary>Голос члена: 1 — за, −1 — против, 0 — воздержался.</summary>
    public static int Vote(IVoteView view, DiplomacyBalance b, ResolutionRecord r, string proposer, string target, string member)
    {
        if (member == proposer)
            return 1;
        if (member == target)
            return r.Against ? -1 : 1;
        var withProposer = view.Relations(member, proposer);
        var withTarget = view.Relations(member, target);
        var score = r.Against ? (withProposer - withTarget) / 2 : (withProposer + withTarget) / 2;
        if (view.Allies(member, target))
            score += r.Against ? -Solidarity : Solidarity;
        return score > b.VoteThreshold ? 1 : score < -b.VoteThreshold ? -1 : 0;
    }
}

/// <summary>
/// Неизменяемая копия всего, что нужно для голосования, — для прогноза на экране (снимок не должен смотреть в живой
/// мир). Пересоздаётся, только когда отношения пересчитаны.
/// </summary>
public sealed class VoteTable : IVoteView
{
    readonly Dictionary<string, int> _index;
    readonly double[][] _relations;
    readonly HashSet<string> _allies;

    public VoteTable(IReadOnlyList<string> ids, double[][] relations, IEnumerable<string> allies,
        IReadOnlyList<string> assembly, IReadOnlyList<string> security, IReadOnlyCollection<string> permanent)
    {
        _index = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        _relations = relations.Select(row => row.ToArray()).ToArray();
        _allies = allies.ToHashSet(StringComparer.Ordinal);
        Assembly = assembly;
        Security = security;
        Permanent = permanent;
    }

    public double Relations(string a, string b) =>
        a == b ? 100 : _index.TryGetValue(a, out var i) && _index.TryGetValue(b, out var j) ? _relations[i][j] : 0;

    public bool Allies(string a, string b) => _allies.Contains(WorldDiplomacy.Key(a, b));

    public IReadOnlyList<string> Assembly { get; }
    public IReadOnlyList<string> Security { get; }
    public IReadOnlyCollection<string> Permanent { get; }
}

