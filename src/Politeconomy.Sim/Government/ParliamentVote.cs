using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Government;

/// <summary>
/// Итог голосования: голоса правящего блока и оппозиции, сколько нужно для принятия и принят ли проект.
/// Decree — парламента нет, власть принимает решение указом.
/// </summary>
public sealed record VoteResult(int RulingYes, int RulingNo, int OppositionYes, int OppositionNo, int Abstained,
    int Required, bool Passed, bool Decree = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int Yes => RulingYes + OppositionYes;
    [System.Text.Json.Serialization.JsonIgnore]
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
        var noise = (world.Random.NextDouble() * 2 - 1) * b.Noise;
        return Count(b, parliament.Kind, parliament.Seats, parliament.RulingSeats, parliament.Discipline, support, constitutional, noise);
    }

    /// <summary>Ожидаемый итог без случайного разброса — для прогноза на экране законов.</summary>
    public static VoteResult Forecast(ParliamentBalance b, string kind, int seats, int ruling, double discipline, double support,
        bool constitutional) =>
        kind == "none" || seats <= 0
            ? new VoteResult(0, 0, 0, 0, 0, 0, Passed: true, Decree: true)
            : Count(b, kind, seats, ruling, discipline, support, constitutional, 0);

    static VoteResult Count(ParliamentBalance b, string kind, int seats, int rulingSeats, double d, double support, bool constitutional,
        double noise)
    {
        support = Math.Clamp(support, 0, 1);
        var rulingYes = Math.Clamp(d + (1 - d) * support + noise, 0, 1);
        var line = Math.Clamp((support - b.OppositionThreshold) / (1 - b.OppositionThreshold) * 2, 0, 1);
        var oppositionYes = Math.Clamp(d * line + (1 - d) * support + noise, 0, 1);
        if (kind == "formal")
            oppositionYes = Math.Max(oppositionYes, 0.5);

        static int Round(int n, double share) => (int)Math.Round(n * share);
        var rulingVoting = Round(rulingSeats, 1 - b.Abstain);
        var oppositionVoting = Round(seats - rulingSeats, 1 - b.Abstain);
        var ry = Round(rulingVoting, rulingYes);
        var oy = Round(oppositionVoting, oppositionYes);
        var abstained = seats - rulingVoting - oppositionVoting;
        var rn = rulingVoting - ry;
        var on = oppositionVoting - oy;
        var required = constitutional
            ? (int)Math.Ceiling(seats * b.Constitutional)
            : (ry + rn + oy + on) / 2 + 1;
        return new VoteResult(ry, rn, oy, on, abstained, required, ry + oy >= required);
    }
}
