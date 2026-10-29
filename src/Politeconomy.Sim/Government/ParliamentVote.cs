using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Government;

/// <summary>Как проголосовала фракция: партия (null — блок без партии), правящая ли она, голоса за и против.</summary>
public sealed record FactionVote(string? Party, bool Ruling, int Yes, int No);

/// <summary>
/// Итог голосования: голоса правящего блока и оппозиции, сколько нужно для принятия и принят ли проект.
/// Decree — парламента нет, власть принимает решение указом. Factions — голоса по фракциям.
/// </summary>
public sealed record VoteResult(int RulingYes, int RulingNo, int OppositionYes, int OppositionNo, int Abstained,
    int Required, bool Passed, bool Decree = false, IReadOnlyList<FactionVote>? Factions = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int Yes => RulingYes + OppositionYes;
    [System.Text.Json.Serialization.JsonIgnore]
    public int No => RulingNo + OppositionNo;
}

/// <summary>Фракция для подсчёта: места, правящая ли, одобрение проекта среди её избирателей (0–1).</summary>
public readonly record struct FactionSupport(string? Party, int Seats, bool Ruling, double Support);

/// <summary>
/// Голосование в парламенте за проект правительства. Support — одобрение проекта избирателями фракции от 0 до 1
/// (0,5 — все равнодушны). Правящие фракции голосуют по линии правительства, кроме недисциплинированной части,
/// которая голосует по настроению своих избирателей (они доверяют своему правительству: +RulingTrust); оппозиционная фракция голосует против, если проект
/// не популярен у её избирателей. MODEL.md, «Правительство» и «Партии».
/// </summary>
public static class ParliamentVote
{
    static readonly VoteResult DecreeResult = new(0, 0, 0, 0, 0, 0, Passed: true, Decree: true);

    public static VoteResult Cast(World world, Parliament parliament, double support, bool constitutional = false) =>
        Cast(world, parliament, _ => support, constitutional);

    /// <summary>Голосование, где одобрение своё у каждой фракции: support(id партии или null).</summary>
    public static VoteResult Cast(World world, Parliament parliament, Func<string?, double> support, bool constitutional = false)
    {
        var b = world.Data.GovernmentBalance.Parliament;
        if (!parliament.Exists)
            return DecreeResult;
        var noise = (world.Random.NextDouble() * 2 - 1) * b.Noise;
        var factions = parliament.Factions.Select(f => new FactionSupport(f.Party, f.Seats, f.Ruling, support(f.Party))).ToList();
        return Count(b, parliament.Kind, parliament.Seats, parliament.Discipline, factions, constitutional, noise);
    }

    /// <summary>Ожидаемый итог без случайного разброса — для прогноза на экранах законов и бюджета.</summary>
    public static VoteResult Forecast(ParliamentBalance b, string kind, int seats, double discipline, IReadOnlyList<FactionSupport> factions,
        bool constitutional) =>
        kind == "none" || seats <= 0 ? DecreeResult : Count(b, kind, seats, discipline, factions, constitutional, 0);

    static VoteResult Count(ParliamentBalance b, string kind, int seats, double d, IReadOnlyList<FactionSupport> factions, bool constitutional,
        double noise)
    {
        static int Round(int n, double share) => (int)Math.Round(n * share);
        int ry = 0, rn = 0, oy = 0, on = 0, voting = 0;
        var byFaction = new List<FactionVote>(factions.Count);
        foreach (var f in factions)
        {
            var support = Math.Clamp(f.Support, 0, 1);
            double yesShare;
            if (f.Ruling)
                yesShare = Math.Clamp(d + (1 - d) * Math.Min(1, support + b.RulingTrust) + noise, 0, 1);
            else
            {
                var line = Math.Clamp((support - b.OppositionThreshold) / (1 - b.OppositionThreshold) * 2, 0, 1);
                yesShare = Math.Clamp(d * line + (1 - d) * support + noise, 0, 1);
                if (kind == "formal")
                    yesShare = Math.Max(yesShare, 0.5);
            }
            var present = Round(f.Seats, 1 - b.Abstain);
            var yes = Round(present, yesShare);
            var no = present - yes;
            voting += present;
            if (f.Ruling)
                (ry, rn) = (ry + yes, rn + no);
            else
                (oy, on) = (oy + yes, on + no);
            byFaction.Add(new FactionVote(f.Party, f.Ruling, yes, no));
        }
        var abstained = seats - voting;
        var required = constitutional ? (int)Math.Ceiling(seats * b.Constitutional) : voting / 2 + 1;
        return new VoteResult(ry, rn, oy, on, abstained, required, ry + oy >= required, Factions: byFaction);
    }
}
