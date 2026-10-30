using System.Runtime.CompilerServices;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics;

namespace Politeconomy.Sim.Engine;

/// <summary>
/// Партия в снимке. Leader — null, если партию возглавляет глава исполнительной власти (Head = true).
/// Poll — доля в опросах 0–1, Seats — места в парламенте, Voters — доли классов среди её избирателей.
/// </summary>
public sealed record PartySnapshot(
    string Id,
    string Name,
    string Short,
    string Family,
    string Color,
    IReadOnlyList<double> Position,
    CharacterSnapshot? Leader,
    bool Head,
    bool Ruling,
    bool Power,
    bool Loyal,
    double Poll,
    int Seats,
    IReadOnlyList<double> Voters,
    double Organization,
    int Founded);

/// <summary>Опубликованный опрос: дата и доли партий (id → 0–1).</summary>
public sealed record PollSnapshot(DateOnly Date, IReadOnlyDictionary<string, double> Shares);

/// <summary>Итог выборов в снимке (поля — как у <see cref="ElectionResult"/>).</summary>
public sealed record ElectionSnapshot(
    DateOnly Date,
    string Kind,
    double Turnout,
    IReadOnlyDictionary<string, double> Votes,
    IReadOnlyDictionary<string, double>? Runoff,
    IReadOnlyDictionary<string, int> Seats,
    IReadOnlyDictionary<string, string> Candidates,
    string? Winner,
    IReadOnlyDictionary<string, string> Regions,
    bool PowerChanged);

/// <summary>Условия партии на переговорах о коалиции.</summary>
public sealed record OfferSnapshot(string Party, int Seats, string? Law, string? Variant, bool Refuses);

/// <summary>Переговоры о коалиции: до какой даты и условия партий.</summary>
public sealed record TalksSnapshot(DateOnly Date, DateOnly Deadline, IReadOnlyList<OfferSnapshot> Offers);

/// <summary>Обещание партнёру по коалиции.</summary>
public sealed record PromiseSnapshot(string Party, string Law, string Variant, DateOnly Deadline);

/// <summary>Обещание кампании: закон, вариант и срок (null — пока идёт кампания).</summary>
public sealed record PledgeSnapshot(string Law, string Variant, DateOnly? Deadline);

/// <summary>Возможное обещание и его оценка (Gain — прибавка к доле партии, 0–1).</summary>
public sealed record PledgeOptionSnapshot(string Law, string Variant, double Gain);

/// <summary>
/// Дебаты: тема (ось взглядов), вопрос и три ответа (левее, центр, правее), выбранный ответ (−1, 0, +1; null — ещё
/// не ответили) и оценка прибавки к доле от каждого ответа без учёта удачи выступления.
/// </summary>
public sealed record DebateSnapshot(DateOnly Date, int Axis, string Topic, string Question, IReadOnlyList<string> Answers, int? Answer,
    IReadOnlyList<double> Gain);

/// <summary>
/// Кампания партии игрока: выборы, очки штаба (и сколько прибавляется в месяц), цены действий, прибавка к
/// привлекательности у классов (Bonus), доля партии у классов, оценки поездок и агитации, лучшие обещания,
/// фальсификации (уровень, прибавка и риск разоблачения по уровням), дебаты.
/// </summary>
public sealed record CampaignSnapshot(
    DateOnly Election,
    double Points,
    double PointsPerMonth,
    double TripCost,
    double MediaCost,
    double PromiseCost,
    IReadOnlyList<double> Bonus,
    IReadOnlyList<double> ClassShare,
    IReadOnlyList<double> TripGain,
    double MediaGain,
    IReadOnlyList<PledgeOptionSnapshot> Options,
    int Fraud,
    IReadOnlyList<double> FraudShare,
    IReadOnlyList<double> FraudRisk,
    DebateSnapshot? Debate);

/// <summary>
/// Требование группы (поля — как у <see cref="GroupDemand"/>); Text — словами; Current — нынешний уровень для
/// расходов (доля ВВП) и дефицита (% ВВП), для закона — 0.
/// </summary>
public sealed record DemandSnapshot(string Kind, string Target, string? Variant, double Level, DateOnly Since, string Text, double Current);

/// <summary>
/// Группа интересов в снимке: влияние (0–1), одобрение (0–100) и его составляющие, давление (больше нуля — давит,
/// меньше — помогает), поправки к модели от этой группы, взгляды и требования.
/// </summary>
public sealed record GroupSnapshot(
    string Id,
    double Influence,
    double Approval,
    double FromClasses,
    double FromLaws,
    double FromBudget,
    double FromGovernment,
    double FromRepression,
    double FromDemands,
    double Pressure,
    IReadOnlyDictionary<string, double> Effects,
    IReadOnlyList<double> Position,
    IReadOnlyList<DemandSnapshot> Demands);

/// <summary>
/// Протест в снимке: класс (индекс), стадия и её название, повод словами, требование (закон и вариант), сколько человек
/// на улицах, ответила ли власть в этом месяце, шансы переговоров и разгона.
/// </summary>
public sealed record ProtestSnapshot(
    int Id,
    int Class,
    int Stage,
    string StageName,
    string Cause,
    string CauseText,
    string? Law,
    string? Variant,
    DateOnly Started,
    int Months,
    double Participants,
    bool Answered,
    string? Response,
    double NegotiateChance,
    double DisperseChance,
    bool Army);

/// <summary>
/// Силовики: лояльность полиции и армии, репрессии (0–100), можно ли сейчас арестовать оппозицию и кого; угрозы власти —
/// вероятности за месяц (вотум недоверия, импичмент, дворцовый и военный переворот, революция); у хунты — когда она
/// обещает вернуть власть гражданским.
/// </summary>
public sealed record SecuritySnapshot(double PoliceLoyalty, double ArmyLoyalty, double Repression, bool CanArrest, string? ArrestTarget, DateOnly? NextArrest,
    PowerModel.Threats? Threats = null, bool Junta = false, DateOnly? HandOver = null);

/// <summary>
/// Политика страны в снимке: избирательная система и барьер, партии (правящие первыми, затем по опросам),
/// взгляды классов [класс][ось], опросы по месяцам.
/// </summary>
public sealed record PoliticsSnapshot(
    string Electoral,
    double Threshold,
    IReadOnlyList<PartySnapshot> Parties,
    IReadOnlyList<IReadOnlyList<double>> Views,
    IReadOnlyList<PollSnapshot> Polls,
    DateOnly? NextElection = null,
    DateOnly? NextPresident = null,
    IReadOnlyList<ElectionSnapshot>? Elections = null,
    TalksSnapshot? Talks = null,
    IReadOnlyList<PromiseSnapshot>? Promises = null,
    bool CanCallEarlyElection = false,
    bool TwoRounds = false,
    CampaignSnapshot? Campaign = null,
    IReadOnlyList<PledgeSnapshot>? Pledges = null,
    double Scandal = 0,
    IReadOnlyList<GroupSnapshot>? Groups = null,
    IReadOnlyList<ProtestSnapshot>? Protests = null,
    SecuritySnapshot? Security = null,
    double Unrest = 0,
    IReadOnlyList<PowerChange>? Changes = null)
{
    public PartySnapshot? Party(string? id) => id == null ? null : Parties.FirstOrDefault(p => p.Id == id);

    /// <summary>Партия главы исполнительной власти.</summary>
    public PartySnapshot? Head => Parties.FirstOrDefault(p => p.Head);

    /// <summary>Последние выборы (любые).</summary>
    public ElectionSnapshot? LastElection => Elections is { Count: > 0 } e ? e[^1] : null;

    static readonly ConditionalWeakTable<CountryPolitics, Cached> Cache = new();

    sealed record Cached(int Version, PoliticsSnapshot Snapshot);

    public static PoliticsSnapshot Capture(World world, Country country, CountryPolitics p)
    {
        if (Cache.TryGetValue(p, out var cached) && cached.Version == p.Version)
            return cached.Snapshot;
        var record = world.Data.PoliticsOf(country.Id);
        var g = world.Government?.Countries.GetValueOrDefault(country.Id);
        var seats = g?.Parliament.Factions.Where(f => f.Party != null).ToDictionary(f => f.Party!, f => f.Seats, StringComparer.Ordinal)
                    ?? new Dictionary<string, int>(StringComparer.Ordinal);
        var head = g?.Leader.Party;
        var parties = p.Parties
            .OrderByDescending(x => x.Ruling).ThenByDescending(x => x.Poll)
            .Select(x => new PartySnapshot(x.Id, x.Name, x.Short, x.Family, x.Color, x.Position.ToArray(),
                x.Leader == null ? null : CharacterSnapshot.Of(x.Leader, world.Date), x.Id == head, x.Ruling, x.Power, x.Loyal, x.Poll,
                seats.GetValueOrDefault(x.Id), x.Voters.ToArray(), x.Organization, x.Founded))
            .ToList();
        var snapshot = new PoliticsSnapshot(
            record.Electoral,
            record.Threshold,
            parties,
            p.Views.Select(v => (IReadOnlyList<double>)v.ToArray()).ToList(),
            p.Polls.Select(x => new PollSnapshot(x.Date, new Dictionary<string, double>(x.Shares, StringComparer.Ordinal))).ToList(),
            p.NextElection,
            p.NextPresident,
            p.Elections.Select(e => new ElectionSnapshot(e.Date, e.Kind, e.Turnout, new Dictionary<string, double>(e.Votes),
                e.Runoff == null ? null : new Dictionary<string, double>(e.Runoff), new Dictionary<string, int>(e.Seats),
                new Dictionary<string, string>(e.Candidates), e.Winner, e.Regions, e.PowerChanged)).ToList(),
            p.Talks == null ? null : new TalksSnapshot(p.Talks.Date, p.Talks.Deadline,
                p.Talks.Offers.Select(o => new OfferSnapshot(o.Party, o.Seats, o.Law, o.Variant, o.Refuses)).ToList()),
            p.Promises.Select(x => new PromiseSnapshot(x.Party, x.Law, x.Variant, x.Deadline)).ToList(),
            PoliticsActions.CanCallEarlyElection(world, country),
            record.TwoRounds,
            CaptureCampaign(world, country, p),
            p.Pledges.Select(x => new PledgeSnapshot(x.Law, x.Variant, x.Deadline)).ToList(),
            p.Scandal,
            CaptureGroups(world, country, p),
            CaptureProtests(world, country, p),
            new SecuritySnapshot(p.Security.PoliceLoyalty, p.Security.ArmyLoyalty, p.Security.Repression, ProtestModel.CanArrest(world, country.Id),
                ProtestModel.Target(p)?.Id, p.Security.Arrests?.AddMonths(world.Data.UnrestBalance.ArrestCooldown),
                PowerModel.Estimate(world, country, p), p.Junta != null, p.Junta?.HandOver),
            p.Unrest,
            p.Changes.ToList());
        Cache.AddOrUpdate(p, new Cached(p.Version, snapshot));
        return snapshot;
    }

    static List<ProtestSnapshot> CaptureProtests(World world, Country country, CountryPolitics p)
    {
        var data = world.Data;
        if (p.Protests.Count == 0 || world.Society?.Countries.GetValueOrDefault(country.Id) is not { } s)
            return [];
        return p.Protests.Select(x => new ProtestSnapshot(x.Id, x.Class, x.Stage, ProtestModel.StageName(data, x), x.Cause, ProtestModel.CauseText(data, x),
            x.Law, x.Variant, x.Started, x.Months, ProtestModel.Participants(data, s, x), x.Answered == world.Date, x.Response,
            ProtestModel.NegotiateChance(data.UnrestBalance, s, x), ProtestModel.DisperseChance(world, country, p, x),
            x.Stage >= data.UnrestBalance.ArmyStage)).ToList();
    }

    static List<GroupSnapshot> CaptureGroups(World world, Country country, CountryPolitics p)
    {
        var data = world.Data;
        var b = data.GroupsBalance;
        var e = world.Economy?.Countries.GetValueOrDefault(country.Id);
        var balance = country.Stat("budget_balance_pct_gdp");
        var list = new List<GroupSnapshot>(p.Groups.Count);
        for (var i = 0; i < p.Groups.Count && i < data.InterestGroups.Groups.Count; i++)
        {
            var g = p.Groups[i];
            var r = data.InterestGroups.Groups[i];
            var pressure = InterestGroupsModel.Pressure(b, g.Approval) * InterestGroupsModel.Weight(b, g.Influence, p.Groups.Count);
            var demands = g.Demands.Select(d => new DemandSnapshot(d.Kind, d.Target, d.Variant, d.Level, d.Since, InterestGroupsModel.Describe(data, d),
                d.Kind switch
                {
                    "spending" when e != null && data.Budget.Spending.ToList().FindIndex(s => s.Id == d.Target) is var k && k >= 0 && k < e.SpendingPlan.Length
                        => e.SpendingPlan[k],
                    "deficit" => double.IsFinite(balance) ? -balance : 0,
                    _ => 0,
                })).ToList();
            list.Add(new GroupSnapshot(g.Id, g.Influence, g.Approval, g.FromClasses, g.FromLaws, g.FromBudget, g.FromGovernment, g.FromRepression, g.Memory, pressure,
                r.Effects.ToDictionary(kv => kv.Key, kv => kv.Value * pressure, StringComparer.Ordinal), g.Position.ToArray(), demands));
        }
        return list;
    }

    static CampaignSnapshot? CaptureCampaign(World world, Country country, CountryPolitics p)
    {
        if (p.Campaign is not { } c || p.Party(c.Party) is not { } party || CampaignModel.Estimate(world, country) is not { } outlook)
            return null;
        var b = world.Data.PoliticsBalance;
        DebateSnapshot? debate = null;
        if (c.Debate is { } d)
        {
            var axis = world.Data.Parties.Axes[d.Axis];
            debate = new DebateSnapshot(d.Date, d.Axis, axis.Name, axis.Debate, [axis.LowAnswer, axis.CenterAnswer, axis.HighAnswer], d.Answer, outlook.DebateGain);
        }
        return new CampaignSnapshot(c.Election, c.Points, CampaignModel.PointsPerMonth(b, party), b.TripCost, b.MediaCost, CampaignModel.PromiseCost(b, p, c),
            party.Campaign.ToArray(), outlook.ClassShare, outlook.TripGain, outlook.MediaGain,
            outlook.Options.Select(o => new PledgeOptionSnapshot(o.Law, o.Variant, o.Gain)).ToList(),
            c.Fraud, b.FraudShare.ToArray(), Enumerable.Range(0, b.FraudShare.Count).Select(l => CampaignModel.FraudRisk(world, country, l)).ToArray(),
            debate);
    }
}
