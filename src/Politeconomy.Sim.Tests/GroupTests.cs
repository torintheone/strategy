using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics;

namespace Politeconomy.Sim.Tests;

public class GroupTests
{
    static readonly Lazy<World> Start = new(() => GameSetup.NewGame(TestData.Game, 3).Simulation.World);

    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 3).Simulation;

    static InterestGroup Group(World world, string country, string id) => world.Politics![country].Groups.Single(g => g.Id == id);

    /// <summary>Принять закон сразу (без парламента) — как будто он прошёл.</summary>
    static void Enact(World world, string country, string law, string variant)
    {
        var laws = world.Laws![country];
        laws.Current[law] = variant;
        LawModel.Recompute(world.Data, laws, world.Date);
    }

    [Fact]
    public void InfluenceFollowsEconomyRegimeAndFaith()
    {
        var world = Start.Value;
        double Influence(string country, string group) => Group(world, country, group).Influence;
        foreach (var p in world.Politics!.Countries.Values)
            Assert.InRange(p.Groups.Sum(g => g.Influence), 0.999, 1.001);
        Assert.True(Influence("SAU", "resource_lobby") > 3 * Influence("DEU", "resource_lobby"), "нефть — сила сырьевого лобби");
        Assert.True(Influence("RUS", "security") > 2 * Influence("DEU", "security"), "при авторитарном режиме силовики сильнее");
        Assert.True(Influence("MMR", "security") > Influence("MMR", "industrialists"), "при военном режиме силовики сильнее всех капиталистов");
        Assert.True(Influence("NGA", "religious") > 3 * Influence("SWE", "religious"), "религиозность страны");
        Assert.True(Influence("GBR", "financiers") > Influence("NGA", "financiers"));
        Assert.True(Influence("IND", "agrarians") > 5 * Influence("GBR", "agrarians"));
    }

    [Fact]
    public void CalmStartWithDemands()
    {
        var world = Start.Value;
        foreach (var p in world.Politics!.Countries.Values)
            Assert.All(p.Groups, g => Assert.InRange(g.Approval, 25, 75));
        var deu = world.Politics!["DEU"];
        Assert.All(deu.Groups, g => Assert.InRange(g.Demands.Count, 1, 3));
        Assert.Contains(deu.Groups.Single(g => g.Id == "unions").Demands, d => d.Kind == "law");
        Assert.Contains(deu.Groups.Single(g => g.Id == "security").Demands, d => d.Kind == "spending");
    }

    [Fact]
    public void FulfilledDemandPleasesTheGroup()
    {
        var sim = NewGame();
        var baseline = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "DEU");
        var unions = Group(world, "DEU", "unions");
        var demand = unions.Demands.First(d => d.Kind == "law");
        Enact(world, "DEU", demand.Target, demand.Variant!);
        sim.RunUntil(world.Date.AddMonths(3));
        baseline.RunUntil(baseline.World.Date.AddMonths(3));
        Assert.DoesNotContain(unions.Demands, d => d.Target == demand.Target);
        Assert.Contains(world.Journal, e => e.Country == "DEU" && e.Kind == "group" && e.Text.StartsWith("Профсоюзы довольны"));
        Assert.True(unions.Approval > Group(baseline.World, "DEU", "unions").Approval + 3);
    }

    [Fact]
    public void SpendingDemandIsMetByTheBudget()
    {
        var sim = NewGame();
        var world = sim.World;
        var security = Group(world, "DEU", "security");
        var demand = security.Demands.Single(d => d.Kind == "spending");
        var k = TestData.Game.Budget.Spending.ToList().FindIndex(s => s.Id == demand.Target);
        world.Economy!["DEU"].SpendingPlan[k] = demand.Level * 1.01;
        sim.RunUntil(world.Date.AddMonths(2));
        Assert.DoesNotContain(security.Demands, d => d.Kind == "spending" && d.Since == TestData.Game.Scenario.StartDate);
        Assert.True(security.FromBudget > 0);
    }

    [Fact]
    public void AngryFinanciersPullCapitalOut()
    {
        var sim = NewGame();
        var baseline = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "GBR");
        Enact(world, "GBR", "capital_controls", "controls");
        Enact(world, "GBR", "wealth_tax", "high");
        Enact(world, "GBR", "state_ownership", "nationalization");
        Enact(world, "GBR", "central_bank", "government");
        sim.RunUntil(world.Date.AddMonths(8));
        baseline.RunUntil(baseline.World.Date.AddMonths(8));
        var financiers = Group(world, "GBR", "financiers");
        Assert.True(financiers.Approval < 40, $"одобрение финансистов {financiers.Approval:F1}: классы {financiers.FromClasses:F1}, законы {financiers.FromLaws:F1}, бюджет {financiers.FromBudget:F1}, власть {financiers.FromGovernment:F1}, требования {financiers.Memory:F1}");
        Assert.True(world.Politics!["GBR"].GroupEffects.GetValueOrDefault("spread") > 0.05);
        Assert.Contains(world.Journal, e => e.Country == "GBR" && e.Kind == "group" && e.Text.StartsWith("Финансисты недовольны"));
        // Недовольные финансируют оппозицию.
        Assert.Contains(world.Politics!["GBR"].Parties, x => !x.Ruling && x.Backing > 0);
        Assert.True(Group(baseline.World, "GBR", "financiers").Approval > financiers.Approval + 10);
    }

    [Fact]
    public void GroupsLobbyInParliament()
    {
        var world = Start.Value;
        var data = TestData.Game;
        var unions = data.Law("unions");
        var nationalization = data.Law("state_ownership");
        var deu = world.Laws!["DEU"].Current;
        var usa = world.Laws!["USA"].Current;
        // Национализацию в США лоббисты капитала встречают в штыки; коллективные договоры в Германии — профсоюзы за.
        Assert.True(InterestGroupsModel.Lobby(world, "USA", nationalization, usa["state_ownership"], "nationalization") < -0.02);
        if (deu["unions"] != "collective")
            Assert.True(InterestGroupsModel.Stance(data, data.InterestGroups.Groups.Single(g => g.Id == "unions"), unions, deu["unions"], "collective") > 1);
    }

    [Fact]
    public void ReligiousAndIntellectualDiscontentHurtsLegitimacy()
    {
        var sim = NewGame();
        var baseline = NewGame();
        var world = sim.World;
        Enact(world, "BRA", "press_freedom", "censorship");
        Enact(world, "BRA", "internet", "sovereign");
        Enact(world, "BRA", "judicial_independence", "dependent");
        Enact(world, "BRA", "tobacco_alcohol", "mild");
        Enact(world, "BRA", "child_benefits", "none");
        sim.RunUntil(world.Date.AddMonths(8));
        baseline.RunUntil(baseline.World.Date.AddMonths(8));
        Assert.True(world.Politics!["BRA"].GroupEffects.GetValueOrDefault("legitimacy") < -0.5);
    }
}
