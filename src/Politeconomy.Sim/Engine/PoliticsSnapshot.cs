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
    bool TwoRounds = false)
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
            record.TwoRounds);
        Cache.AddOrUpdate(p, new Cached(p.Version, snapshot));
        return snapshot;
    }
}
