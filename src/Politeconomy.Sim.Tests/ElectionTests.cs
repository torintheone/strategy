using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim.Tests;

public class ElectionTests
{
    static Simulation NewGame(ulong seed = 3) => GameSetup.NewGame(TestData.Game, seed).Simulation;

    [Fact]
    public void ElectionsFollowTheRealCalendar()
    {
        var sim = NewGame();
        var bra = sim.World.Politics!["BRA"];
        Assert.Equal(new DateOnly(2026, 10, 1), bra.NextElection);
        Assert.Equal(new DateOnly(2026, 10, 1), bra.NextPresident);
        Assert.Equal(new DateOnly(2029, 3, 1), sim.World.Politics!["DEU"].NextElection);
        Assert.Null(sim.World.Politics!["DEU"].NextPresident);
        // До старта уже были выборы — их итог нужен для карты и для сравнения.
        Assert.Single(bra.Elections);
        Assert.True(bra.Elections[0].Date < sim.World.Date);

        sim.RunUntil(new DateOnly(2026, 10, 2));
        Assert.Equal(3, bra.Elections.Count);
        Assert.Contains(bra.Elections, e => e is { Kind: "president" } && e.Date == new DateOnly(2026, 10, 1));
        Assert.Equal(new DateOnly(2030, 10, 1), bra.NextElection);
        Assert.Equal(new DateOnly(2030, 10, 1), bra.NextPresident);
        Assert.Contains(sim.World.Journal, e => e.Country == "BRA" && e.Kind == "election");
    }

    [Fact]
    public void ElectionResultsArePlausible()
    {
        // Критерий приёмки этапа 4: выборы дают правдоподобные результаты.
        var sim = NewGame();
        sim.RunUntil(sim.World.Date.AddYears(4));
        var results = sim.World.Countries
            .Where(c => c.Regime == Regime.Democracy)
            .SelectMany(c => sim.World.Politics![c.Id].Elections.Skip(1).Select(e => (Country: c, Result: e)))
            .ToList();
        Assert.True(results.Count > 80, $"за 4 года выборов в демократиях: {results.Count}");
        foreach (var (country, r) in results)
        {
            Assert.InRange(r.Turnout, 0.3, 0.95);
            Assert.InRange(r.Votes.Values.Sum(), 0.99, 1.01);
            // У парламентских выборов в демократии разгромов больше 70% не бывает; у президентских — сильный кандидат возможен.
            Assert.InRange(r.Votes.Values.Max(), 0.12, r.Kind == "parliament" ? 0.7 : 0.85);
            if (r.Kind == "parliament")
                Assert.Equal(sim.World.Government![country.Id].Parliament.Seats, r.Seats.Values.Sum());
            if (r.Runoff != null)
                Assert.Equal(2, r.Runoff.Count);
        }
        // Власть меняется иногда, но не всегда.
        var changed = results.Count(x => x.Result.PowerChanged);
        Assert.InRange(changed, results.Count / 10, results.Count * 3 / 4);
        // Регионы есть у всех итогов, и в больших странах побеждают разные партии.
        Assert.All(results, x => Assert.NotEmpty(x.Result.Regions));
        Assert.Contains(results, x => x.Result.Regions.Values.Distinct().Count() > 1);
    }

    [Fact]
    public void MajoritarianSystemsRewardTheWinner()
    {
        var sim = NewGame();
        sim.RunUntil(new DateOnly(2029, 8, 2));
        var gbr = sim.World.Politics!["GBR"].Elections[^1];
        Assert.Equal("parliament", gbr.Kind);
        var winner = gbr.Winner!;
        Assert.True((double)gbr.Seats[winner] / gbr.Seats.Values.Sum() > gbr.Votes[winner], "в мажоритарной системе победитель получает больше мест, чем голосов");
    }

    [Fact]
    public void PresidentialRunoffDecidesBetweenTwo()
    {
        var sim = NewGame();
        var p = sim.World.Politics!["BRA"];
        // Сделать выборы равными: никто не наберёт половины в первом туре.
        foreach (var party in p.Parties)
            party.Momentum = 0;
        sim.RunUntil(new DateOnly(2026, 10, 2));
        var president = p.Elections.Last(e => e.Kind == "president");
        if (president.Votes.Values.Max() <= 0.5)
        {
            Assert.NotNull(president.Runoff);
            Assert.Contains(president.Winner!, president.Runoff!.Keys);
            Assert.True(president.Runoff![president.Winner!] >= 0.5);
        }
        else
            Assert.Null(president.Runoff);
        var head = sim.World.Government!["BRA"].Leader;
        Assert.Equal(president.Winner, head.Party);
        Assert.Equal(president.Candidates[president.Winner!], head.Name);
    }

    [Fact]
    public void TermLimitedPresidentHandsOverToASuccessor()
    {
        var sim = NewGame();
        var world = sim.World;
        Assert.Equal("two", world.Laws!["USA"].Current["term_limits"]);
        var president = world.Government!["USA"].Leader;
        president.Terms = 2;
        sim.RunUntil(new DateOnly(2028, 11, 2));
        Assert.NotEqual(president, world.Government!["USA"].Leader);
        Assert.Equal(1, world.Government!["USA"].Leader.Terms);
        var result = world.Politics!["USA"].Elections.Last(e => e.Kind == "president");
        Assert.DoesNotContain(president.Name, result.Candidates.Values);
    }

    [Fact]
    public void LosingAnElectionAsksThePlayer()
    {
        var sim = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "BRA");
        // Правящая партия в провале: скандал за скандалом.
        foreach (var party in world.Politics!["BRA"].Parties.Where(x => x.Ruling))
            party.Momentum = -4;
        var oldLeader = world.Government!["BRA"].Leader.Name;
        for (var date = world.Date; date < new DateOnly(2026, 10, 2); date = date.AddMonths(1))
        {
            foreach (var party in world.Politics!["BRA"].Parties.Where(x => x.Ruling))
                party.Momentum = -4;
            sim.RunUntil(date.AddMonths(1));
        }
        Assert.NotNull(world.Defeat);
        Assert.Equal(oldLeader, world.Defeat!.OldLeader);
        Assert.Equal(world.Government!["BRA"].Leader.Name, world.Defeat.NewLeader);
        Assert.Equal(1, world.Reign!.ElectionsLost);

        PoliticsActions.ContinueAfterDefeat(world);
        Assert.Null(world.Defeat);
        Assert.Equal("BRA", world.PlayerCountry);

        PoliticsActions.EndReign(world);
        Assert.Null(world.PlayerCountry);
        Assert.Equal("BRA", world.LastReign!.Country);
        Assert.Equal(new DateOnly(2026, 1, 1), world.LastReign.Since);
        Assert.True(world.LastReign.End.ContainsKey("gdp_per_capita_usd"));
    }

    /// <summary>Переговоры после выборов в стране игрока: партия главы без большинства (запускаем напрямую).</summary>
    static (Simulation Sim, CountryPolitics P) Talks(string id)
    {
        var sim = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, id);
        var p = world.Politics![id];
        var head = PoliticsModel.HeadParty(world, id)!;
        var seats = p.Parties.Select(x => world.Government![id].Parliament.Factions.FirstOrDefault(f => f.Party == x.Id)?.Seats ?? 0).ToArray();
        CoalitionTalksModel.Start(world, world[id], head, seats);
        return (sim, p);
    }

    [Fact]
    public void PlayerNegotiatesACoalitionAndMustKeepPromises()
    {
        const string id = "NLD";
        var (sim, p) = Talks(id);
        var world = sim.World;
        Assert.NotNull(p.Talks);
        Assert.Contains(world.Journal, e => e.Country == id && e.Kind == "coalition");
        // Слишком далёкие по взглядам отказываются, с ними коалицию не собрать.
        if (p.Talks!.Offers.FirstOrDefault(o => o.Refuses) is { } refusing)
            Assert.False(PoliticsActions.FormCoalition(world, id, [refusing.Party]));
        var offer = p.Talks.Offers.Where(o => !o.Refuses && o.Law != null).OrderByDescending(o => o.Seats).First();
        Assert.True(PoliticsActions.FormCoalition(world, id, [offer.Party]));
        Assert.Null(p.Talks);
        Assert.True(p.Party(offer.Party)!.Ruling);
        Assert.Contains(world.Government![id].Ministers.Values, m => m.Party == offer.Party);
        var promise = Assert.Single(p.Promises, x => x.Party == offer.Party);
        Assert.Equal(offer.Law, promise.Law);

        // Обещание не выполнено за два года — партнёр уходит.
        sim.RunUntil(promise.Deadline.AddMonths(1).AddDays(1));
        Assert.NotEqual(promise.Variant, world.Laws![id].Current[promise.Law]);
        Assert.False(p.Party(offer.Party)!.Ruling);
        Assert.DoesNotContain(world.Government![id].Ministers.Values, m => m.Party == offer.Party);
        Assert.Contains(world.Journal, e => e.Country == id && e.Text.Contains("выходит из коалиции"));
    }

    [Fact]
    public void KeptPromiseKeepsThePartner()
    {
        const string id = "NLD";
        var (sim, p) = Talks(id);
        var world = sim.World;
        var offer = p.Talks!.Offers.Where(o => !o.Refuses && o.Law != null).OrderByDescending(o => o.Seats).First();
        PoliticsActions.FormCoalition(world, id, [offer.Party]);
        // Закон принят — обещание выполнено, партнёр остаётся.
        world.Laws![id].Current[offer.Law!] = offer.Variant!;
        sim.RunUntil(world.Date.AddMonths(1).AddDays(1));
        Assert.Empty(p.Promises);
        Assert.True(p.Party(offer.Party)!.Ruling);
        Assert.Contains(world.Journal, e => e.Country == id && e.Text.Contains("выполнено"));
    }

    [Fact]
    public void ExpiredTalksFormACoalitionWithoutThePlayer()
    {
        const string id = "NLD";
        var (sim, p) = Talks(id);
        var deadline = p.Talks!.Deadline;
        sim.RunUntil(deadline.AddDays(1));
        Assert.Null(p.Talks);
        Assert.Contains(sim.World.Journal, e => e.Country == id && e.Kind == "coalition" && (e.Text.Contains("Коалиция сложилась") || e.Text.Contains("меньшинства")));
    }

    [Fact]
    public void EarlyElectionComesInTwoMonths()
    {
        var sim = NewGame();
        var world = sim.World;
        Assert.False(PoliticsActions.CallEarlyElection(world, "USA"), "в президентской республике парламент не распустить");
        Assert.True(PoliticsActions.CallEarlyElection(world, "SWE"));
        var p = world.Politics!["SWE"];
        Assert.Equal(new DateOnly(2026, 3, 1), p.NextElection);
        sim.RunUntil(new DateOnly(2026, 3, 2));
        Assert.Equal(new DateOnly(2026, 3, 1), p.Elections[^1].Date);
        Assert.Equal(new DateOnly(2030, 3, 1), p.NextElection);
    }

    [Fact]
    public void PoliticsSurvivesSaveAndLoad()
    {
        var sim = NewGame();
        sim.RunUntil(new DateOnly(2026, 10, 2));
        using var stream = new MemoryStream();
        SaveGame.Write(sim.World, stream);
        stream.Position = 0;
        var loaded = SaveGame.Read(TestData.Game, stream);
        Assert.Equal(SaveGame.Hash(sim.World), SaveGame.Hash(loaded));
        // И дальше живут одинаково: генератор политики сохранился.
        var resumed = GameSetup.Resume(loaded);
        sim.RunUntil(new DateOnly(2027, 3, 1));
        resumed.RunUntil(new DateOnly(2027, 3, 1));
        Assert.Equal(SaveGame.Hash(sim.World), SaveGame.Hash(resumed.World));
    }
}
