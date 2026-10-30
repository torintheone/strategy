using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Tests;

public class EducationTests
{
    const string Id = "DEU";

    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 3).Simulation;

    static int Education => TestData.Game.Budget.Spending.ToList().FindIndex(s => s.Id == "education");

    static double Professionals(World world)
    {
        var s = world.Society![Id];
        var workers = s.Heads.Sum() - s.Heads[ClassIds.Pensioners] - s.Heads[ClassIds.Unemployed];
        return s.Heads[ClassIds.Professionals] / workers;
    }

    [Fact]
    public void StartMatchesTheData()
    {
        var world = NewGame().World;
        foreach (var country in world.Countries)
        {
            var e = world.Education![country.Id];
            Assert.Equal(e.Tertiary, e.TertiaryStock);
            Assert.Equal(1, e.HumanCapital);
            Assert.InRange(e.Quality, 10, 95);
            Assert.Equal(100, country.Stat("human_capital"), 6);
        }
    }

    [Fact]
    public void EducationSpendingRaisesSkillsYearsLater()
    {
        // Критерий приёмки этапа 5: рост расходов на образование меняет квалификацию работников с задержкой в годы.
        var more = NewGame();
        var same = NewGame();
        more.World.Economy![Id].SpendingPlan[Education] *= 1.6;
        more.RunUntil(more.World.Date.AddYears(2));
        same.RunUntil(same.World.Date.AddYears(2));
        var (a, b) = (more.World.Education![Id], same.World.Education![Id]);
        Assert.True(a.Quality > b.Quality * 1.05, $"качество {a.Quality:F1} против {b.Quality:F1}");
        Assert.True(a.Tertiary > b.Tertiary * 1.05, "охват растёт за пару лет");
        Assert.True(a.TertiaryStock < b.TertiaryStock * 1.02, "а работники почти те же");
        Assert.True(Professionals(more.World) < Professionals(same.World) * 1.004, "специалистов пока столько же");

        more.RunUntil(more.World.Date.AddYears(18));
        same.RunUntil(same.World.Date.AddYears(18));
        Assert.True(a.TertiaryStock > b.TertiaryStock * 1.04, $"через 20 лет высшее в рабочей силе {a.TertiaryStock:F1} против {b.TertiaryStock:F1}");
        Assert.True(Professionals(more.World) > Professionals(same.World) * 1.01, $"специалистов {Professionals(more.World):P2} против {Professionals(same.World):P2}, запас {a.TertiaryStock:F1}/{b.TertiaryStock:F1}");
        Assert.True(a.HumanCapital > b.HumanCapital, "человеческий капитал выше");
    }

    [Fact]
    public void CuttingEducationErodesQuality()
    {
        var less = NewGame();
        var same = NewGame();
        less.World.Economy![Id].SpendingPlan[Education] *= 0.5;
        less.RunUntil(less.World.Date.AddYears(3));
        same.RunUntil(same.World.Date.AddYears(3));
        Assert.True(less.World[Id].Stat("education_quality") < same.World[Id].Stat("education_quality") - 5);
        Assert.True(less.World.Education![Id].Tertiary < same.World.Education![Id].Tertiary);
    }

    [Fact]
    public void FreeUniversitiesExpandEnrollmentGradually()
    {
        var free = NewGame();
        var world = free.World;
        var laws = world.Laws!["USA"];
        Assert.Equal("paid", laws.Current["higher_education"]);
        laws.Current["higher_education"] = "free";
        Laws.LawModel.Recompute(world.Data, laws, world.Date);
        var start = world.Education!["USA"].Tertiary;
        free.RunUntil(world.Date.AddMonths(6));
        var half = world.Education!["USA"].Tertiary;
        free.RunUntil(world.Date.AddYears(5));
        var later = world.Education!["USA"].Tertiary;
        Assert.True(half > start && later > half * 1.1, $"{start:F1} → {half:F1} → {later:F1}");
    }
}
