using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Diplomacy;

// «Совет Наций»: Общее собрание (все признанные страны) и Совет безопасности (5 постоянных + 10 выборных). MODEL.md, «Совет Наций».
public static partial class DiplomacyModel
{
    /// <summary>Члены Совета Наций — признанные страны.</summary>
    public static IReadOnlyList<string> Assembly(World world) =>
        world.Countries.Where(c => c.Info.UnMember).Select(c => c.Id).ToList();

    public static IReadOnlyList<string> Permanent(World world) =>
        world.Data.Diplomacy.Council.Permanent.Where(id => world.ById(id) is not null).ToList();

    /// <summary>Совет безопасности: постоянные члены и выборные.</summary>
    public static IReadOnlyList<string> Security(World world) =>
        Permanent(world).Concat(world.Diplomacy?.Seats.Select(s => s.Country) ?? []).ToList();

    sealed class WorldVoteView(World world) : IVoteView
    {
        public double Relations(string a, string b) => DiplomacyModel.Relations(world, a, b);
        public bool Allies(string a, string b) => DiplomacyModel.Allies(world, a, b);
        public IReadOnlyList<string> Assembly { get; } = DiplomacyModel.Assembly(world);
        public IReadOnlyList<string> Security { get; } = DiplomacyModel.Security(world);
        public IReadOnlyCollection<string> Permanent { get; } = DiplomacyModel.Permanent(world);
    }

    /// <summary>Копия для прогноза голосования на экране; новая — только когда отношения пересчитаны.</summary>
    public static VoteTable Votes(World world)
    {
        var d = world.Diplomacy!;
        if (d.VoteCache is { } cache && cache.Version == d.Version)
            return cache.Table;
        var allies = new List<string>();
        foreach (var bloc in world.Data.Diplomacy.Blocs.Where(b => b.Military))
        {
            var members = Members(world, bloc.Id);
            for (var x = 0; x < members.Count; x++)
                for (var y = x + 1; y < members.Count; y++)
                    allies.Add(WorldDiplomacy.Key(members[x], members[y]));
        }
        allies.AddRange(d.Treaties.Where(t => t.Type == "alliance").Select(t => WorldDiplomacy.Key(t.A, t.B)));
        var table = new VoteTable(d.Ids, d.Relations, allies, Assembly(world), Security(world), Permanent(world));
        d.VoteCache = (d.Version, table);
        return table;
    }

    /// <summary>Как проголосуют за резолюцию сейчас.</summary>
    public static VoteCount Forecast(World world, string type, string proposer, string target) =>
        CouncilVoting.Count(new WorldVoteView(world), world.Data.DiplomacyBalance, world.Data.Diplomacy.Council.Resolution(type)!, proposer, target);

    /// <summary>Можно ли вынести резолюцию и почему нет: раз в CouncilMonths, только член Совета, у каждого вида свои условия.</summary>
    public static (bool Allowed, string Reason) CanPropose(World world, string proposer, string type, string target)
    {
        var data = world.Data;
        var d = world.Diplomacy!;
        if (data.Diplomacy.Council.Resolution(type) is null)
            return (false, "Такой резолюции нет.");
        if (proposer == target || world.ById(target) is null)
            return (false, "Нужна другая страна.");
        if (!world[proposer].Info.UnMember)
            return (false, $"Страна не член «{data.Diplomacy.Council.Name}».");
        if (d.Proposed.TryGetValue(proposer, out var last) && last.AddMonths(data.DiplomacyBalance.CouncilMonths) > world.Date)
            return (false, $"Следующую резолюцию можно вынести с {Fmt.Date(last.AddMonths(data.DiplomacyBalance.CouncilMonths))}.");
        return type switch
        {
            "sanctions" when UnderCouncilSanctions(world, target) => (false, "Страна уже под санкциями Совета."),
            "lift" when !UnderCouncilSanctions(world, target) => (false, "Санкций Совета против страны нет."),
            "aid" when world[target].Income is not (IncomeGroup.Low or IncomeGroup.LowerMiddle) => (false, "Помощь — только бедным странам."),
            _ => (true, ""),
        };
    }

    /// <summary>Вынести резолюцию (игрок): члены голосуют; если принята — она действует.</summary>
    public static bool ProposeResolution(World world, string proposer, string type, string target)
    {
        if (world.PlayerCountry != proposer || !CanPropose(world, proposer, type, target).Allowed)
            return false;
        var data = world.Data;
        var b = data.DiplomacyBalance;
        var d = world.Diplomacy!;
        var record = data.Diplomacy.Council.Resolution(type)!;
        var vote = Forecast(world, type, proposer, target);
        d.Proposed[proposer] = world.Date;
        d.Resolutions.Add(new Resolution(world.Date, type, proposer, target, vote.Yes, vote.No, vote.Abstain, vote.Passed, vote.Vetoes));
        if (d.Resolutions.Count > 30)
            d.Resolutions.RemoveRange(0, d.Resolutions.Count - 30);
        var title = $"«{record.Name}: {world[target].Name}»";
        var tally = $"за {vote.Yes}, против {vote.No}, воздержались {vote.Abstain}";
        if (!vote.Passed)
        {
            var why = vote.Vetoes.Count > 0 ? $"вето — {string.Join(", ", vote.Vetoes.Select(v => world[v].Name))}" : "не хватило голосов";
            world.Log(proposer, "council", $"{data.Diplomacy.Council.Name}: резолюция {title} не принята ({why}): {tally}.");
            Refresh(world);
            return false;
        }
        switch (type)
        {
            case "condemn":
                foreach (var v in vote.YesVoters.Where(v => v != target))
                    Remember(world, target, v, -b.CondemnRelations);
                break;
            case "sanctions":
                d.CouncilSanctions.Add(target);
                break;
            case "lift":
                d.CouncilSanctions.Remove(target);
                break;
            case "aid":
                CouncilAid(world, target, vote.YesVoters);
                break;
        }
        world.Log(proposer, "council", $"{data.Diplomacy.Council.Name}: резолюция {title} принята: {tally}.");
        Refresh(world);
        return true;
    }

    /// <summary>Помощь Совета: долг цели −CouncilAid % её ВВП; платят богатые страны, голосовавшие «за», по размеру экономики.</summary>
    static void CouncilAid(World world, string target, IReadOnlyList<string> yes)
    {
        var b = world.Data.DiplomacyBalance;
        var e = world.Economy![target];
        var usd = b.CouncilAid / 100 * e.GdpUsd;
        e.Debt = Math.Max(0, e.Debt - b.CouncilAid / 100 * e.NominalGdp);
        var donors = yes.Where(v => v != target && world[v].Income == IncomeGroup.High).ToList();
        var total = donors.Sum(v => world.Economy[v].GdpUsd);
        foreach (var v in donors)
        {
            var donor = world.Economy[v];
            donor.Debt += usd * donor.GdpUsd / total * donor.ExchangeRate;
        }
        foreach (var v in yes.Where(v => v != target))
            Remember(world, target, v, b.CouncilAidRelations);
    }

    /// <summary>
    /// 1 января: выборные члены, чей срок кончился, уступают места странам того же региона. Избирают ту, к которой
    /// лучше всего относятся члены Совета Наций, кроме тех, кто уже был в Совете за последние шесть лет.
    /// </summary>
    static void ElectCouncil(World world)
    {
        var d = world.Diplomacy!;
        var year = world.Date.Year;
        var leaving = d.Seats.Where(s => s.Until < year).ToList();
        if (leaving.Count == 0)
            return;
        var assembly = Assembly(world);
        var permanent = Permanent(world);
        foreach (var seat in leaving)
        {
            d.Seats.Remove(seat);
            d.Served[seat.Country] = seat.Until;
        }
        foreach (var seat in leaving)
        {
            var region = world[seat.Country].Info.Region;
            var chosen = assembly
                .Where(id => world[id].Info.Region == region && !permanent.Contains(id) && d.Seats.All(s => s.Country != id)
                             && (!d.Served.TryGetValue(id, out var served) || served < year - 6))
                .Select(id => (id, score: assembly.Where(m => m != id).Average(m => Relations(world, m, id))))
                .OrderByDescending(x => x.score).ThenBy(x => x.id, StringComparer.Ordinal)
                .FirstOrDefault().id;
            if (chosen == null)
                continue;
            d.Seats.Add(new CouncilSeat(chosen, year + 1));
            world.Log(chosen, "council", $"Страна избрана в Совет безопасности «{world.Data.Diplomacy.Council.Name}» на {year}–{year + 1} годы.");
        }
    }
}
