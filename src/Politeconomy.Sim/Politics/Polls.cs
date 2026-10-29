using Politeconomy.Sim.Data;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Опрос: каждый класс делит голоса между партиями по привлекательности (логит-модель). Привлекательность =
/// − расстояние до взглядов класса / DistanceScale + популярность лидера + организованность + случайный дрейф
/// + ответственность власти (правящим партиям — по удовлетворённости класса и по состоянию экономики) − усталость от власти
/// + административный ресурс партии власти. В мажоритарной демократии мелкие партии теряют «бесполезные» голоса.
/// Голос класса весит числом взрослых в нём (работающие, безработные, пенсионеры). MODEL.md, «Опросы».
/// </summary>
public static class Polls
{
    /// <summary>
    /// Shares — доли партий; Voters — состав избирателей каждой партии по классам; ByClass[класс][партия] — как
    /// делит голоса каждый класс.
    /// </summary>
    public sealed record Result(double[] Shares, double[][] Voters, double[][] ByClass);

    /// <summary>
    /// Опрос среди партий list (по умолчанию все партии страны). Extra — добавка к привлекательности (кандидат,
    /// кампания), weights — вес голоса каждого класса (по умолчанию — число взрослых; на выборах — с учётом явки).
    /// </summary>
    public static Result Compute(PoliticsBalance b, Country country, CountryPolitics p, CountrySociety s, CountryGovernment g, bool majoritarian,
        DateOnly date, double economy, IReadOnlyList<Party>? list = null, Func<Party, double>? extra = null, IReadOnlyList<double>? weights = null)
    {
        var parties = list ?? p.Parties;
        var n = parties.Count;
        // В мажоритарной системе две ведущие партии (по опросам, на старте — по организованности) не теряют голосов,
        // остальные теряют: избиратели не хотят отдавать голос тем, кто не выиграет округ.
        var leading = p.Parties.OrderByDescending(x => x.Poll).ThenByDescending(x => x.Organization).Take(2).ToHashSet();
        var classes = p.Views.Length;
        var votes = new double[n];
        var byParty = new double[n][];
        for (var i = 0; i < n; i++)
            byParty[i] = new double[classes];
        var byClass = new double[classes][];
        var admin = b.AdminBonus[Math.Clamp((int)country.Regime, 0, b.AdminBonus.Count - 1)];
        var bonus = parties.Select(x => extra?.Invoke(x) ?? 0).ToArray();
        var u = new double[n];
        for (var k = 0; k < classes; k++)
        {
            byClass[k] = new double[n];
            var weight = weights != null ? weights[k] : s.Heads[k];
            if (weight <= 0 || n == 0)
                continue;
            for (var i = 0; i < n; i++)
                u[i] = Appeal(b, parties[i], p.Views[k], s.Satisfaction[k], g, admin, date, economy) + bonus[i]
                       - (majoritarian && !leading.Contains(parties[i]) ? b.WastedVote : 0);
            var max = u.Take(n).Max();
            var sum = 0.0;
            for (var i = 0; i < n; i++)
                sum += u[i] = Math.Exp(u[i] - max);
            for (var i = 0; i < n; i++)
            {
                byClass[k][i] = u[i] / sum;
                var v = weight * byClass[k][i];
                votes[i] += v;
                byParty[i][k] = v;
            }
        }
        var total = votes.Sum();
        var shares = votes.Select(v => total > 0 ? v / total : 1.0 / n).ToArray();
        var voters = byParty.Select(row =>
        {
            var t = row.Sum();
            return row.Select(v => t > 0 ? v / t : 0).ToArray();
        }).ToArray();
        return new Result(shares, voters, byClass);
    }

    /// <summary>Привлекательность партии для класса со взглядами view и удовлетворённостью satisfaction.</summary>
    public static double Appeal(PoliticsBalance b, Party party, IReadOnlyList<double> view, double satisfaction, CountryGovernment g, double admin,
        DateOnly date, double economy = 0)
    {
        var leader = party.Leader ?? g.Leader;
        var appeal = -PoliticalViews.Distance(view, party.Position, b.Salience) / b.DistanceScale
                     + b.LeaderWeight * (leader.Popularity - 50) / 50
                     + b.OrganizationWeight * (party.Organization - 0.5)
                     + party.Momentum;
        if (party.Ruling)
            appeal += b.Incumbency * (satisfaction - 50) / 50 + economy - Fatigue(b, party, date);
        if (party.Power)
            appeal += admin;
        return appeal;
    }

    /// <summary>
    /// Экономическое голосование: избиратели судят власть по экономике — разрыв выпуска (спад минус, подъём плюс),
    /// безработица выше естественной и инфляция выше цели больше чем на 2 п. п. (не больше 20 п. п.).
    /// </summary>
    public static double EconomicVote(PoliticsBalance b, Economy.CountryEconomy e) =>
        b.GapWeight * e.Gap * 100
        - b.UnemploymentWeight * Math.Max(0, (e.Unemployment - e.NaturalUnemployment) * 100)
        - b.InflationWeight * Math.Clamp((e.Inflation - e.InflationTarget) * 100 - 2, 0, 20);

    /// <summary>Усталость от власти: FatiguePerYear за каждый год у власти, не больше FatigueMax.</summary>
    public static double Fatigue(PoliticsBalance b, Party party, DateOnly date) =>
        party.RulingSince is { } since ? Math.Min(b.FatigueMax, b.FatiguePerYear * Math.Max(0, date.DayNumber - since.DayNumber) / 365.25) : 0;
}
