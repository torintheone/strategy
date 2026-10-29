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

/// <summary>
/// Политика страны в снимке: избирательная система и барьер, партии (правящие первыми, затем по опросам),
/// взгляды классов [класс][ось], опросы по месяцам.
/// </summary>
public sealed record PoliticsSnapshot(
    string Electoral,
    double Threshold,
    IReadOnlyList<PartySnapshot> Parties,
    IReadOnlyList<IReadOnlyList<double>> Views,
    IReadOnlyList<PollSnapshot> Polls)
{
    public PartySnapshot? Party(string? id) => id == null ? null : Parties.FirstOrDefault(p => p.Id == id);

    /// <summary>Партия главы исполнительной власти.</summary>
    public PartySnapshot? Head => Parties.FirstOrDefault(p => p.Head);

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
            p.Polls.Select(x => new PollSnapshot(x.Date, new Dictionary<string, double>(x.Shares, StringComparer.Ordinal))).ToList());
        Cache.AddOrUpdate(p, new Cached(p.Version, snapshot));
        return snapshot;
    }
}
