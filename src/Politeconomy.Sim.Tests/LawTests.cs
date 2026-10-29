using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim.Tests;

public class LawTests
{
    static Engine.Simulation NewGame(ulong seed = 3) => GameSetup.NewGame(TestData.Game, seed).Simulation;

    /// <summary>Сразу ввести вариант закона без голосования и переходного периода (для проверки эффектов).</summary>
    static void Force(World world, string country, string law, string variant)
    {
        var c = world.Laws![country];
        c.Current[law] = variant;
        LawModel.Recompute(world.Data, c, world.Date);
    }

    [Fact]
    public void StartVariantsFollowCountryRules()
    {
        var world = NewGame().World;
        string V(string country, string law) => world.Laws![country].Current[law];
        Assert.Equal("government", V("RUS", "central_bank"));
        Assert.Equal("independent", V("DEU", "central_bank"));
        Assert.Equal("censorship", V("RUS", "press_freedom"));
        Assert.Equal("free", V("FRA", "press_freedom"));
        Assert.Equal("restricted", V("TUR", "press_freedom"));
        Assert.Equal("weak", V("USA", "employment_protection"));
        Assert.Equal("sovereign", V("CHN", "internet"));
        Assert.Equal("none", V("DEU", "term_limits"));
        Assert.Equal("a63", V("RUS", "retirement_age"));
    }

    [Fact]
    public void AtStartLawsChangeNothing()
    {
        var world = NewGame().World;
        foreach (var c in world.Laws!.Countries.Values)
        {
            Assert.Empty(c.Modifiers);
            Assert.All(c.Attitudes, a => Assert.Equal(0, a));
        }
        Assert.Equal(15, world["RUS"].Stat("democracy_index"), 1);
        Assert.Equal(85, world["DEU"].Stat("democracy_index"), 1);
        Assert.True(world["SOM"].Stat("corruption_index") > world["DNK"].Stat("corruption_index") + 30);
    }

    [Fact]
    public void LawPassesTheWholeCycle()
    {
        // Критерий приёмки этапа 3: подготовка в министерстве → голосование → подпись → переходный период → действие.
        var sim = NewGame();
        var world = sim.World;
        world.PlayerCountry = "BRA";
        var laws = world.Laws!["BRA"];
        Assert.Equal("low", laws.Current["minimum_wage"]);
        Assert.True(LawActions.Propose(world, "BRA", "minimum_wage", "high"));
        Assert.False(LawActions.Propose(world, "BRA", "minimum_wage", "medium"), "второй проект по тому же закону не берут");
        var bill = laws.BillFor("minimum_wage")!;
        Assert.Equal("drafting", bill.Stage);
        Assert.True(bill.Ready > world.Date);

        sim.RunUntil(bill.Ready.AddMonths(1).AddDays(1));
        Assert.Null(laws.BillFor("minimum_wage"));
        Assert.Equal("passed", bill.Stage);
        Assert.True(bill.Vote!.Passed);
        Assert.False(bill.Vote.Decree);
        Assert.Equal("high", laws.Current["minimum_wage"]);
        Assert.Contains(world.Journal, e => e.Country == "BRA" && e.Kind == "law" && e.Text.Contains("принял"));
        Assert.Contains(laws.Finished, b => ReferenceEquals(b, bill));

        // Переходный период: эффект набирает силу постепенно.
        Assert.Single(laws.Transitions);
        var halfway = laws.Effect("natural_unemployment");
        Assert.InRange(halfway, 0.01, 1.19);
        sim.RunUntil(world.Date.AddMonths(TestData.Game.Law("minimum_wage").TransitionMonths + 1));
        Assert.Empty(laws.Transitions);
        Assert.Equal(1.2, laws.Effect("natural_unemployment"), 6);
        Assert.Equal(0.12, laws.Effect("unskilled_wage"), 6);
        var unskilled = TestData.Game.Classes.Classes.ToList().FindIndex(c => c.Id == "unskilled");
        Assert.Equal(3, laws.Attitudes[unskilled], 6);
    }

    [Fact]
    public void UnpopularLawFailsWithoutMajority()
    {
        var sim = NewGame();
        var world = sim.World;
        // Демократия, где правит одна партия главы, и у неё меньше 45% мест: остальные фракции — оппозиция.
        var country = world.Countries.First(c => c.Regime == Regime.Democracy && world.Laws![c.Id].Current["conscription"] == "professional"
                                                 && world.Government![c.Id].Parliament.Factions.FirstOrDefault(f => f.Party == world.Government![c.Id].Leader.Party)
                                                     is { } head && head.Seats < 0.45 * world.Government![c.Id].Parliament.Seats);
        var parliament = world.Government![country.Id].Parliament;
        foreach (var f in parliament.Factions)
            f.Ruling = f.Party == world.Government![country.Id].Leader.Party;
        // Всеобщий призыв не нравится почти никому — оппозиция голосует против.
        Assert.True(LawActions.Propose(world, country.Id, "conscription", "universal"));
        var bill = world.Laws![country.Id].BillFor("conscription")!;
        sim.RunUntil(bill.Ready.AddMonths(1).AddDays(1));
        Assert.Equal("rejected", bill.Stage);
        Assert.Equal("professional", world.Laws![country.Id].Current["conscription"]);
        Assert.Empty(world.Laws![country.Id].Modifiers);
        Assert.Contains(world.Journal, e => e.Country == country.Id && e.Text.Contains("отклонил"));
    }

    [Fact]
    public void MilitaryRegimeRulesByDecree()
    {
        var sim = NewGame();
        var world = sim.World;
        Assert.True(LawActions.Propose(world, "MLI", "unions", "banned"));
        var bill = world.Laws!["MLI"].BillFor("unions")!;
        sim.RunUntil(bill.Ready.AddMonths(1).AddDays(1));
        Assert.True(bill.Vote!.Decree);
        Assert.Equal("banned", world.Laws!["MLI"].Current["unions"]);
    }

    [Fact]
    public void ConstitutionNeedsQualifiedMajority()
    {
        var sim = NewGame();
        var world = sim.World;
        Assert.True(world.Government!["DEU"].Parliament.RulingSeats < world.Government!["DEU"].Parliament.Seats * 2 / 3);
        Assert.True(LawActions.Propose(world, "DEU", "judicial_independence", "dependent"));
        var bill = world.Laws!["DEU"].BillFor("judicial_independence")!;
        sim.RunUntil(bill.Ready.AddMonths(1).AddDays(1));
        Assert.Equal("rejected", bill.Stage);
    }

    [Fact]
    public void CompetentMinisterDraftsFaster()
    {
        var world = NewGame().World;
        var law = TestData.Game.Law("pension_system");
        world.Government!["FRA"].Ministers["labor"].Competence = 95;
        world.Government!["FRA"].Ministers["labor"].Traits.Clear();
        var fast = Legislation.DraftingMonths(world, "FRA", law);
        world.Government!["FRA"].Ministers["labor"].Competence = 15;
        var slow = Legislation.DraftingMonths(world, "FRA", law);
        Assert.True(fast < slow, $"{fast} против {slow}");
    }

    [Fact]
    public void LawEffectsReachTheModel()
    {
        var a = NewGame(5);
        var b = NewGame(5);
        Force(a.World, "DEU", "retirement_age", "a60");
        Force(a.World, "DEU", "health_system", "private");
        Force(a.World, "DEU", "press_freedom", "censorship");
        a.RunUntil(a.World.Date.AddMonths(2));
        b.RunUntil(b.World.Date.AddMonths(2));
        var x = a.World.Economy!["DEU"];
        var y = b.World.Economy!["DEU"];
        Assert.True(x.LaborForce < y.LaborForce * 0.995, "ранняя пенсия уменьшает рабочую силу");
        var health = TestData.Game.Budget.Spending.ToList().FindIndex(s => s.Id == "health");
        Assert.True(x.Spending[health] < y.Spending[health] * 0.9, "частная медицина снижает расходы бюджета");
        Assert.Equal(a.World["DEU"].Stat("press_freedom_index"), b.World["DEU"].Stat("press_freedom_index") - 45, 1);
        var professionals = TestData.Game.Classes.Classes.ToList().FindIndex(c => c.Id == "professionals");
        Assert.True(a.World.Society!["DEU"].Satisfaction[professionals] < b.World.Society!["DEU"].Satisfaction[professionals] - 1,
            "цензура злит специалистов");
    }

    [Fact]
    public void BillsSurviveSaveAndLoad()
    {
        var sim = NewGame(11);
        LawActions.Propose(sim.World, "IND", "child_benefits", "generous");
        sim.RunUntil(sim.World.Date.AddMonths(2));
        using var stream = new MemoryStream();
        SaveGame.Write(sim.World, stream);
        stream.Position = 0;
        var loaded = GameSetup.Resume(SaveGame.Read(TestData.Game, stream));
        Assert.Equal(SaveGame.Hash(sim.World), SaveGame.Hash(loaded.World));
        sim.RunUntil(sim.World.Date.AddMonths(12));
        loaded.RunUntil(loaded.World.Date.AddMonths(12));
        Assert.Equal(SaveGame.Hash(sim.World), SaveGame.Hash(loaded.World));
    }
}
