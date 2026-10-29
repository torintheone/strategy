using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Government;

/// <summary>
/// Итог голосования: голоса правящего блока и оппозиции, сколько нужно для принятия и принят ли проект.
/// Decree — парламента нет, власть принимает решение указом.
/// </summary>
public sealed record VoteResult(int RulingYes, int RulingNo, int OppositionYes, int OppositionNo, int Abstained,
    int Required, bool Passed, bool Decree = false)
{
    public int Yes => RulingYes + OppositionYes;
    public int No => RulingNo + OppositionNo;
}

/// <summary>
/// Голосование в парламенте за проект правительства. Support — одобрение проекта в обществе от 0 до 1
/// (0,5 — все равнодушны). Правящий блок голосует по линии правительства, кроме недисциплинированной части,
/// которая голосует по настроению избирателей; оппозиция голосует против, если проект не популярен.
/// MODEL.md, «Правительство».
/// </summary>
public static class ParliamentVote
{
    public static VoteResult Cast(World world, Parliament parliament, double support, bool constitutional = false)
    {
        var b = world.Data.GovernmentBalance.Parliament;
        if (!parliament.Exists)
            return new VoteResult(0, 0, 0, 0, 0, 0, Passed: true, Decree: true);
        support = Math.Clamp(support, 0, 1);
        var d = parliament.Discipline;
        var noise = (world.Random.NextDouble() * 2 - 1) * b.Noise;
        var rulingYes = Math.Clamp(d + (1 - d) * support + noise, 0, 1);
        var line = Math.Clamp((support - b.OppositionThreshold) / (1 - b.OppositionThreshold) * 2, 0, 1);
        var oppositionYes = Math.Clamp(d * line + (1 - d) * support + noise, 0, 1);
        if (parliament.Kind == "formal")
            oppositionYes = Math.Max(oppositionYes, 0.5);

        int Count(int seats, double share) => (int)Math.Round(seats * share);
        var rulingVoting = Count(parliament.RulingSeats, 1 - b.Abstain);
        var oppositionVoting = Count(parliament.OppositionSeats, 1 - b.Abstain);
        var ry = Count(rulingVoting, rulingYes);
        var oy = Count(oppositionVoting, oppositionYes);
        var abstained = parliament.Seats - rulingVoting - oppositionVoting;
        var rn = rulingVoting - ry;
        var on = oppositionVoting - oy;
        var required = constitutional
            ? (int)Math.Ceiling(parliament.Seats * b.Constitutional)
            : (ry + rn + oy + on) / 2 + 1;
        return new VoteResult(ry, rn, oy, on, abstained, required, ry + oy >= required);
    }
}
