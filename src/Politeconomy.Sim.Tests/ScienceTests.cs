using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Science;

namespace Politeconomy.Sim.Tests;

public class ScienceTests
{
    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 3).Simulation;

    static Data.TechnologyRecord Tech(string id) => TestData.Game.Science.Technologies.First(t => t.Id == id);

    static int Branch(string id) => TestData.Game.Science.Branches.ToList().FindIndex(b => b.Id == id);

    [Fact]
    public void RichCountriesKnowMoreAndNobodyKnowsTheFuture()
    {
        var world = NewGame().World;
        var s = world.Science!;
        Assert.True(s["USA"].Researched.Count > s["IND"].Researched.Count);
        Assert.True(s["IND"].Researched.Count > s["ETH"].Researched.Count);
        Assert.All(TestData.Game.Science.Technologies.Where(t => t.Tier >= 6), t => Assert.Equal(0, s.Adoption[t.Id]));
        Assert.True(s.Adoption["antibiotics"] > s.Adoption["gene_therapy"]);
        Assert.All(world.Countries, c =>
        {
            var x = s[c.Id];
            Assert.All(x.Productivity.Values, v => Assert.Equal(1, v, 9));
            Assert.All(x.Effects.Values, v => Assert.Equal(0, v, 9));
        });
    }

    [Fact]
    public void KnownTechnologyIsCheaperThanDiscovery()
    {
        var world = NewGame().World;
        var india = world["IND"];
        var b = TestData.Game.ScienceBalance;
        Assert.Equal(b.FrontierCost * b.TierCost[5], ScienceModel.Cost(world, india, Tech("ai")), 6);
        Assert.True(ScienceModel.Cost(world, india, Tech("targeted_drugs")) < ScienceModel.Cost(world, india, Tech("ai")) * 0.2);
    }

    [Fact]
    public void MoreMoneyForScienceMeansFasterResearchAndGrowth()
    {
        var more = NewGame();
        var same = NewGame();
        const string id = "IND";
        PoliticsActions.PlayAs(more.World, id);
        PoliticsActions.PlayAs(same.World, id);
        var item = TestData.Game.Budget.Spending.ToList().FindIndex(s => s.Id == "science");
        more.World.Economy![id].SpendingPlan[item] *= 6;
        more.RunUntil(more.World.Date.AddYears(10));
        same.RunUntil(same.World.Date.AddYears(10));
        var (a, b) = (more.World.Science![id], same.World.Science![id]);
        Assert.True(a.Researched.Count > b.Researched.Count, $"технологий {a.Researched.Count} против {b.Researched.Count}");
        Assert.True(a.Effects["potential_growth"] > 0.1, $"к росту {a.Effects["potential_growth"]:F2}");
    }

    [Fact]
    public void PlayerSetsPriorityAndAllocation()
    {
        var sim = NewGame();
        var same = NewGame();
        var world = sim.World;
        const string id = "IND";
        Assert.False(ScienceModel.Allocate(world, id, "medicine", 0.5), "только в своей стране");
        PoliticsActions.PlayAs(world, id);
        Assert.False(ScienceModel.Prioritize(world, id, "ai"), "сначала нужны предыдущие уровни");
        var target = ScienceModel.Target(TestData.Game, world.Science![id], "industry")!;
        Assert.True(ScienceModel.Prioritize(world, id, target.Id));
        Assert.True(ScienceModel.Allocate(world, id, "medicine", 0.6));
        var a = world.Science![id].Allocation;
        Assert.Equal(1, a.Sum(), 9);
        Assert.Equal(0.725, a[Branch("medicine")], 6);
        sim.RunUntil(world.Date.AddYears(12));
        same.RunUntil(same.World.Date.AddYears(12));
        Assert.Contains(target.Id, world.Science![id].Researched);
        Assert.DoesNotContain(target.Id, same.World.Science![id].Researched);
        int Medicine(World w) => w.Science![id].Researched.Count(t => Tech(t).Branch == "medicine");
        Assert.True(Medicine(world) > Medicine(same.World));
    }

    [Fact]
    public void CensorshipDrivesScientistsAway()
    {
        var sim = NewGame();
        var same = NewGame();
        const string id = "DEU";
        var laws = sim.World.Laws![id];
        laws.Current["press_freedom"] = "censorship";
        laws.Current["assembly"] = "banned";
        Laws.LawModel.Recompute(sim.World.Data, laws, sim.World.Date);
        sim.RunUntil(sim.World.Date.AddYears(2));
        same.RunUntil(same.World.Date.AddYears(2));
        Assert.True(sim.World.Science![id].Efficiency < same.World.Science![id].Efficiency * 0.95);
        Assert.True(sim.World.Science![id].Drain > same.World.Science![id].Drain);
    }

    [Fact]
    public void DiscoveryGivesAnEdgeThatFadesAsTheWorldCatchesUp()
    {
        var sim = NewGame();
        var world = sim.World;
        world.Science!["USA"].Researched.Add("big_data");
        world.Science!["USA"].Researched.Add("ai");
        sim.RunUntil(world.Date.AddMonths(1));
        var edge = world.Science!["USA"].Productivity["services"];
        Assert.True(edge > 1.03, $"производительность услуг {edge:F3}");
        foreach (var c in world.Science.Countries.Values)
        {
            c.Researched.Add("big_data");
            c.Researched.Add("ai");
        }
        sim.RunUntil(world.Date.AddMonths(1));
        Assert.True(world.Science!["USA"].Productivity["services"] < 1.005);
    }

    [Fact]
    public void DigitalStateAndAtomicPowerReachTheModel()
    {
        var sim = NewGame();
        var world = sim.World;
        const string id = "ETH";
        Assert.DoesNotContain("e_documents", world.Science![id].Researched);
        world.Science![id].Researched.Add("e_documents");
        world.Science![id].Researched.Add("nuclear_power");
        sim.RunUntil(world.Date.AddMonths(1));
        Assert.Contains(world[id].Stats["corruption_index"].Factors!, f => f.Label == "Цифровое государство" && f.Value < -1.5);
        var nuclear = TestData.Game.Energy.Plants.ToList().FindIndex(p => p.Id == "nuclear");
        Assert.True(Energy.EnergyModel.Weights(world, world[id], world.Energy![id], 0)[nuclear] > 0, "атомную станцию теперь можно строить");
    }
}
