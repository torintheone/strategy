using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Health;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Tests;

public class HealthTests
{
    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 3).Simulation;

    static int Item(string id) => TestData.Game.Budget.Spending.ToList().FindIndex(s => s.Id == id);

    [Fact]
    public void StartFollowsIncomeAndSystem()
    {
        var world = NewGame().World;
        Assert.True(world.Health!["DEU"].Doctors > 3 * world.Health!["NGA"].Doctors);
        Assert.True(world.Health!["GBR"].Access > world.Health!["USA"].Access, "государственная система доступнее частной");
        Assert.All(world.Countries, c => Assert.Equal(1, world.Health![c.Id].Index, 6));
        Assert.Contains("vaccination", world.Programs!["DEU"].Current);
        Assert.Empty(world.Programs!["DEU"].Effects);
    }

    [Fact]
    public void MoreMoneyForMedicineMeansMoreDoctorsLongerLivesLaterAndFewerSickDays()
    {
        var more = NewGame();
        var same = NewGame();
        const string id = "IND";
        more.World.Economy![id].SpendingPlan[Item("health")] *= 2;
        more.RunUntil(more.World.Date.AddYears(1));
        same.RunUntil(same.World.Date.AddYears(1));
        var (a, b) = (more.World.Health![id], same.World.Health![id]);
        Assert.True(a.Doctors > b.Doctors && a.Doctors < b.Doctors * 1.15, "врачей готовят годами");
        more.RunUntil(more.World.Date.AddYears(9));
        same.RunUntil(same.World.Date.AddYears(9));
        Assert.True(a.Doctors > b.Doctors * 1.3, $"через 10 лет врачей {a.Doctors:F2} против {b.Doctors:F2}");
        Assert.True(a.Burden < b.Burden);
        Assert.True(more.World.Demography![id].LifeExpectancy > same.World.Demography![id].LifeExpectancy + 0.5);
    }

    [Fact]
    public void StateHealthcareWidensAccess()
    {
        var sim = NewGame();
        var world = sim.World;
        var laws = world.Laws!["USA"];
        Assert.Equal("private", laws.Current["health_system"]);
        var before = world.Health!["USA"].Access;
        laws.Current["health_system"] = "state";
        Laws.LawModel.Recompute(world.Data, laws, world.Date);
        sim.RunUntil(world.Date.AddYears(3));
        Assert.True(world.Health!["USA"].Access > before + 10);
        Assert.True(world.Health!["USA"].Index > 1.03);
    }

    [Fact]
    public void ProgramsCostMoneyAndWork()
    {
        var sim = NewGame();
        var world = sim.World;
        Assert.False(ProgramModel.Toggle(world, "NGA", "rural_medicine"), "только в своей стране");
        Politics.PoliticsActions.PlayAs(world, "NGA");
        Assert.True(ProgramModel.Toggle(world, "NGA", "rural_medicine"));
        var effects = world.Programs!["NGA"].Effects;
        Assert.Equal(0.2, effects["spending.health"], 6);
        Assert.Equal(8, effects["health_access"], 6);
        Assert.Contains(world.Journal, e => e.Country == "NGA" && e.Kind == "program");
        var access = world.Health!["NGA"].Access;
        sim.RunUntil(world.Date.AddYears(2));
        Assert.True(world.Health!["NGA"].Access > access + 5);
        // Высокотехнологичная помощь без врачей почти бесполезна.
        Assert.True(ProgramModel.Toggle(world, "NGA", "high_tech_care"));
        Assert.True(world.Programs!["NGA"].Effects["life_expectancy"] < 0.3 + 0.6);
        // Стартовую программу можно закрыть — бюджет сэкономит.
        Assert.True(ProgramModel.Toggle(world, "NGA", "vaccination"));
        Assert.True(world.Programs!["NGA"].Effects.GetValueOrDefault("disease_burden") > 0);
    }

    [Fact]
    public void AgeingRaisesPensionSpending()
    {
        var sim = NewGame();
        var world = sim.World;
        sim.RunUntil(world.Date.AddYears(15));
        // Япония и Корея стареют: доля пожилых растёт, пенсий платят больше.
        Assert.True(world.Health!["KOR"].Effects["spending.social"] > 0.5);
        Assert.True(Laws.LawModel.Effect(world, "KOR", "spending.social") > 0.5);
    }
}
