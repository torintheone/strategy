using System.Runtime.CompilerServices;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Laws;

namespace Politeconomy.Sim.Engine;

/// <summary>Законопроект в снимке: закон, вариант, стадия (drafting, passed, rejected, withdrawn), даты, одобрение, голосование.</summary>
public sealed record BillSnapshot(string Law, string Variant, string Stage, DateOnly Started, DateOnly Ready, DateOnly? Decided,
    double Support, VoteResult? Vote);

/// <summary>Законы страны в снимке: действующие варианты, варианты на старте, законопроекты, переходные периоды (доля пройденного).</summary>
public sealed record LawsSnapshot(
    IReadOnlyDictionary<string, string> Current,
    IReadOnlyDictionary<string, string> Start,
    IReadOnlyList<BillSnapshot> Bills,
    IReadOnlyList<BillSnapshot> Finished,
    IReadOnlyDictionary<string, double> Transitions)
{
    public BillSnapshot? BillFor(string law) => Bills.FirstOrDefault(b => b.Law == law);

    static readonly ConditionalWeakTable<CountryLaws, Cached> Cache = new();

    sealed record Cached(int Version, LawsSnapshot Snapshot);

    public static LawsSnapshot Capture(DateOnly date, CountryLaws c)
    {
        if (Cache.TryGetValue(c, out var cached) && cached.Version == c.Version)
            return cached.Snapshot;
        static BillSnapshot Bill(Bill b) => new(b.Law, b.Variant, b.Stage, b.Started, b.Ready, b.Decided, b.Support, b.Vote);
        var snapshot = new LawsSnapshot(
            new Dictionary<string, string>(c.Current, StringComparer.Ordinal),
            new Dictionary<string, string>(c.Start, StringComparer.Ordinal),
            c.Bills.Select(Bill).ToList(),
            c.Finished.Select(Bill).ToList(),
            c.Transitions.ToDictionary(t => t.Law, t => t.Progress(date), StringComparer.Ordinal));
        Cache.AddOrUpdate(c, new Cached(c.Version, snapshot));
        return snapshot;
    }
}
