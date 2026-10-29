using Politeconomy.Sim.Budget;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim.Tests;

public class BudgetTests
{
    static Engine.Simulation Play(string country, ulong seed = 3)
    {
        var sim = GameSetup.NewGame(TestData.Game, seed).Simulation;
        sim.World.PlayerCountry = country;
        return sim;
    }

    static int Index(string revenue) => TestData.Game.Budget.Revenues.ToList().FindIndex(r => r.Id == revenue);
    static int Spending(string id) => TestData.Game.Budget.Spending.ToList().FindIndex(s => s.Id == id);

    [Fact]
    public void BudgetIsDraftedInSeptemberVotedInOctoberAndAppliedInJanuary()
    {
        var sim = Play("DEU");
        var world = sim.World;
        sim.RunUntil(new DateOnly(2026, 9, 2));
        var p = world.Budgets!["DEU"];
        Assert.Equal(2027, p.Year);
        Assert.Equal("draft", p.Stage);
        Assert.NotNull(p.Forecast);
        sim.RunUntil(new DateOnly(2026, 10, 2));
        Assert.Equal("passed", p.Stage);
        Assert.Contains(world.Journal, e => e.Country == "DEU" && e.Kind == "budget" && e.Text.Contains("принял бюджет на 2027"));
        var approved = p.Spending.ToArray();
        sim.RunUntil(new DateOnly(2027, 1, 2));
        Assert.Equal(approved, world.Economy!["DEU"].SpendingPlan);
        Assert.Equal("none", p.Stage);
        Assert.Equal(0, world.Economy!["DEU"].BudgetFailedYear);
    }

    [Fact]
    public void FailedBudgetLeavesLastYearsBudgetInForce()
    {
        // Критерий приёмки этапа 3: провал бюджета в парламенте отрабатывается корректно.
        var sim = Play("DEU");
        var baseline = GameSetup.NewGame(TestData.Game, 3).Simulation;
        baseline.World.PlayerCountry = "DEU";
        var world = sim.World;
        // У правительства меньшинство, а проект резко повышает налоги — парламент его не примет.
        var parliament = world.Government!["DEU"].Parliament;
        parliament.Factions = Parliament.Blocks(parliament.Kind, parliament.Name, parliament.Seats, 250, parliament.Discipline).Factions;
        sim.RunUntil(new DateOnly(2026, 9, 2));
        var e = world.Economy!["DEU"];
        var p = world.Budgets!["DEU"];
        var taxes = p.Taxes.ToArray();
        taxes[Index("pit")] = 1.6;
        taxes[Index("social")] = 1.6;
        taxes[Index("vat")] = 1.6;
        Assert.True(BudgetActions.Edit(world, "DEU", p.Spending.ToArray(), taxes));
        Assert.True(p.Forecast!.Support < 0.35, $"одобрение {p.Forecast.Support:F2}");
        var planBefore = e.SpendingPlan.ToArray();
        var policyBefore = e.TaxPolicy.ToArray();

        sim.RunUntil(new DateOnly(2026, 10, 2));
        Assert.Equal("rejected", p.Stage);
        Assert.Equal(new DateOnly(2026, 11, 1), p.NextVote);
        sim.RunUntil(new DateOnly(2026, 12, 2));
        Assert.Equal("failed", p.Stage);
        Assert.Equal(3, p.Votes.Count);
        Assert.Equal(planBefore, e.SpendingPlan);

        sim.RunUntil(new DateOnly(2027, 2, 2));
        baseline.RunUntil(new DateOnly(2027, 2, 2));
        Assert.Equal(2027, e.BudgetFailedYear);
        Assert.Equal(planBefore, e.SpendingPlan);
        Assert.Equal(policyBefore, e.TaxPolicy);
        Assert.True(e.Rating < baseline.World.Economy!["DEU"].Rating - 0.5, "без бюджета рейтинг ниже");
        Assert.Contains(world.Journal, x => x.Country == "DEU" && x.Text.Contains("так и не принят"));

        // Жизнь продолжается: в сентябре — новый проект.
        sim.RunUntil(new DateOnly(2027, 9, 2));
        Assert.Equal(2028, p.Year);
        Assert.Equal("draft", p.Stage);
        Assert.False(double.IsNaN(e.NominalGdp));
    }

    [Fact]
    public void AmendmentChangesCurrentBudget()
    {
        var sim = Play("FRA");
        var world = sim.World;
        sim.RunUntil(new DateOnly(2026, 3, 2));
        var e = world.Economy!["FRA"];
        var spending = e.SpendingPlan.ToArray();
        spending[Spending("health")] *= 1.1;
        Assert.True(BudgetActions.Edit(world, "FRA", spending, e.TaxPolicy.ToArray()));
        Assert.True(BudgetActions.SubmitAmendment(world, "FRA"));
        sim.RunUntil(new DateOnly(2026, 4, 2));
        Assert.Equal(spending[Spending("health")], e.SpendingPlan[Spending("health")], 9);
        Assert.Contains(world.Journal, x => x.Country == "FRA" && x.Text.Contains("поправки"));
    }

    [Fact]
    public void ForecastShowsWhoGainsAndWhoLoses()
    {
        var sim = Play("ITA");
        var world = sim.World;
        sim.RunUntil(new DateOnly(2026, 2, 2));
        var e = world.Economy!["ITA"];
        var classes = TestData.Game.Classes.Classes.ToList();
        var spending = e.SpendingPlan.ToArray();
        spending[Spending("social")] *= 1.3;
        var taxes = e.TaxPolicy.ToArray();
        taxes[Index("pit")] = 1.4;
        var f = BudgetCycle.Forecast(world, "ITA", spending, taxes);
        Assert.True(f.Attitudes[classes.FindIndex(c => c.Id == "pensioners")] > 0, "пенсионеры за рост соцрасходов");
        Assert.True(f.IncomeChange[classes.FindIndex(c => c.Id == "professionals")] < 0, "специалисты платят больше налога");
        Assert.True(f.Spending > world.Economy!["ITA"].TotalSpending / world.Economy!["ITA"].NominalGdp);
    }

    [Fact]
    public void ObedientCentralBankFollowsGovernmentRate()
    {
        var sim = Play("RUS");
        var world = sim.World;
        Assert.True(BudgetActions.CanSetRate(world, "RUS"));
        Assert.False(BudgetActions.CanSetRate(world, "DEU"));
        Assert.True(BudgetActions.SetRate(world, "RUS", 0.05));
        sim.RunUntil(world.Date.AddMonths(18));
        Assert.InRange(world.Economy!["RUS"].PolicyRate, 0.045, 0.06);
    }

    [Fact]
    public void BudgetProcessSurvivesSaveAndLoad()
    {
        var sim = Play("ESP", 7);
        sim.RunUntil(new DateOnly(2026, 9, 15));
        using var stream = new MemoryStream();
        SaveGame.Write(sim.World, stream);
        stream.Position = 0;
        var loaded = GameSetup.Resume(SaveGame.Read(TestData.Game, stream));
        Assert.Equal(SaveGame.Hash(sim.World), SaveGame.Hash(loaded.World));
        sim.RunUntil(new DateOnly(2027, 2, 1));
        loaded.RunUntil(new DateOnly(2027, 2, 1));
        Assert.Equal(SaveGame.Hash(sim.World), SaveGame.Hash(loaded.World));
    }
}
