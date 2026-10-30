using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Infrastructure;
using Politeconomy.Sim.Politics;

namespace Politeconomy.Sim.Tests;

public class InfrastructureTests
{
    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 3).Simulation;

    static int Type(string id) => TestData.Game.Infrastructure.Types.ToList().FindIndex(t => t.Id == id);

    static int Item(string id) => TestData.Game.Budget.Spending.ToList().FindIndex(s => s.Id == id);

    [Fact]
    public void StartIsBalanced()
    {
        var world = NewGame().World;
        Assert.All(world.Countries, c =>
        {
            var x = world.Infrastructure![c.Id];
            Assert.All(x.Coverage, v => Assert.Equal(1, v, 9));
            Assert.All(x.Cut, v => Assert.Equal(0, v, 9));
            Assert.True(c.Stat("infrastructure_index") > 100, c.Id);
        });
        Assert.True(world["DEU"].Stat("infrastructure_condition_pct") > world["NGA"].Stat("infrastructure_condition_pct"));
        Assert.Equal(0, world.Infrastructure!["ETH"].Capacity[Type("ports")]);
    }

    [Fact]
    public void NeglectedRoadsDecayAndChokeTheEconomy()
    {
        var cut = NewGame();
        var same = NewGame();
        const string id = "BRA";
        PoliticsActions.PlayAs(cut.World, id);
        PoliticsActions.PlayAs(same.World, id);
        cut.World.Economy![id].SpendingPlan[Item("transport")] *= 0.2;
        cut.RunUntil(cut.World.Date.AddYears(12));
        same.RunUntil(same.World.Date.AddYears(12));
        var (a, b) = (cut.World.Infrastructure![id], same.World.Infrastructure![id]);
        var roads = Type("roads");
        Assert.True(a.Condition[roads] < b.Condition[roads] - 0.15, $"состояние дорог {a.Condition[roads]:P0} против {b.Condition[roads]:P0}");
        Assert.True(a.Coverage[roads] < 0.97, $"обеспеченность дорогами {a.Coverage[roads]:P1}");
        Assert.True(a.Cut.Max() > 0.01);
        Assert.True(cut.World.Economy![id].RealGdp < same.World.Economy![id].RealGdp * 0.99);
        Assert.True(cut.World[id].Stat("infrastructure_index") < same.World[id].Stat("infrastructure_index"));
    }

    [Fact]
    public void BudgetProjectCostsMoneyWhileBuiltThenAddsCapacity()
    {
        var sim = NewGame();
        var world = sim.World;
        const string id = "DEU";
        Assert.False(InfrastructureModel.Start(world, id, "highways", "budget"), "только в своей стране");
        PoliticsActions.PlayAs(world, id);
        var roads = Type("roads");
        var before = world.Infrastructure![id].Capacity[roads];
        Assert.True(InfrastructureModel.Start(world, id, "highways", "budget"));
        var project = world.Infrastructure![id].Projects.Single();
        Assert.True(Laws.LawModel.Effect(world, id, "spending.transport") > 0.5, "стройка идёт из бюджета транспорта");
        Assert.Contains(world.Journal, e => e.Country == id && e.Kind == "project");
        sim.RunUntil(project.Ends.AddMonths(1));
        Assert.True(project.Done);
        Assert.True(world.Infrastructure![id].Capacity[roads] > before + project.Amount * 0.9);
        Assert.False(world.Infrastructure![id].Effects.ContainsKey("spending.transport"));
    }

    [Fact]
    public void LoanRaisesDebtAtOnceAndRemembersTheLender()
    {
        var sim = NewGame();
        var world = sim.World;
        const string id = "KEN";
        PoliticsActions.PlayAs(world, id);
        var e = world.Economy![id];
        var debt = e.Debt;
        Assert.False(InfrastructureModel.Start(world, id, "railway", "loan", "KEN"), "сам себе не кредитор");
        Assert.True(InfrastructureModel.Start(world, id, "railway", "loan", "CHN"));
        var project = world.Infrastructure![id].Projects.Single();
        Assert.Equal(debt + project.Cost * e.NominalGdp, e.Debt, 6);
        Assert.Equal("CHN", world.Infrastructure![id].Loans.Single().Lender);
        Assert.False(world.Infrastructure![id].Effects.ContainsKey("spending.transport"), "бюджет за стройку не платит");
        Assert.True(e.ProjectFlow > 0, "но заказы стройке идут");
    }

    [Fact]
    public void ConcessionNeedsCleanGovernmentAndUsersPay()
    {
        var sim = NewGame();
        var world = sim.World;
        const string id = "DNK";
        PoliticsActions.PlayAs(world, id);
        Assert.False(InfrastructureModel.Start(world, id, "road_repair", "ppp"), "ремонт в концессию не берут");
        Assert.True(InfrastructureModel.Start(world, id, "airport", "ppp"));
        var project = world.Infrastructure![id].Projects.Single();
        sim.RunUntil(project.Ends.AddMonths(1));
        Assert.NotNull(project.Concession);
        Assert.True(world.Infrastructure![id].Mood.All(m => m < 0));

        // Где коррупция высока, инвесторы не приходят, а стройки дороже и дольше.
        var corrupt = world.Countries.First(c => c.Stat("corruption_index") > 70);
        var offer = InfrastructureModel.Quote(world, corrupt, TestData.Game.Infrastructure.Projects.First(p => p.Id == "airport"));
        var clean = InfrastructureModel.Quote(world, world[id], TestData.Game.Infrastructure.Projects.First(p => p.Id == "airport"));
        Assert.False(offer.PppAllowed);
        Assert.True(offer.Cost > clean.Cost * 1.2 && offer.Months > clean.Months);
    }

    [Fact]
    public void PowerPlantProjectAddsCapacity()
    {
        var sim = NewGame();
        var world = sim.World;
        const string id = "POL";
        PoliticsActions.PlayAs(world, id);
        var nuclear = TestData.Game.Energy.Plants.ToList().FindIndex(p => p.Id == "nuclear");
        Assert.True(InfrastructureModel.Start(world, id, "nuclear_plant", "budget"));
        var project = world.Infrastructure![id].Projects.Single();
        Assert.True(project.Ends > world.Date.AddYears(7), "АЭС строят годами");
        Assert.True(Laws.LawModel.Effect(world, id, "spending.energy") > 0);
        sim.RunUntil(project.Ends.AddMonths(1));
        Assert.True(world.Energy![id].Capacity[nuclear] >= project.Amount * 0.99);
    }

    [Fact]
    public void BrokenWaterSystemsMakePeopleSick()
    {
        var sim = NewGame();
        var same = NewGame();
        const string id = "IND";
        sim.World.Infrastructure![id].Capacity[Type("water")] *= 0.5;
        sim.RunUntil(sim.World.Date.AddMonths(3));
        same.RunUntil(same.World.Date.AddMonths(3));
        Assert.True(sim.World.Infrastructure![id].Effects["disease_burden"] > 0.1);
        Assert.True(sim.World.Health![id].Burden > same.World.Health![id].Burden);
    }
}
