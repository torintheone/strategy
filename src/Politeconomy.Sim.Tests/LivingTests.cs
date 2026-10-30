using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Health;

namespace Politeconomy.Sim.Tests;

public class LivingTests
{
    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 3).Simulation;

    static int Item(string id) => TestData.Game.Budget.Spending.ToList().FindIndex(s => s.Id == id);

    [Fact]
    public void StartFollowsIncome()
    {
        var world = NewGame().World;
        var (deu, nga) = (world.Living!["DEU"], world.Living!["NGA"]);
        Assert.True(deu.Housing > 2 * nga.Housing);
        Assert.True(nga.Crime > deu.Crime);
        Assert.All(world.Countries, c =>
        {
            var l = world.Living![c.Id];
            Assert.Equal(l.Desired, l.Housing, 6);
            Assert.Empty(l.Effects);
            Assert.InRange(c.Stat("pollution_index"), 5, 95);
            Assert.Equal(100, c.Stat("housing_price_index"), 6);
        });
    }

    [Fact]
    public void LessPoliceMoneyMeansMoreCrime()
    {
        var cut = NewGame();
        var same = NewGame();
        const string id = "BRA";
        cut.World.Economy![id].SpendingPlan[Item("police")] *= 0.5;
        cut.RunUntil(cut.World.Date.AddYears(3));
        same.RunUntil(same.World.Date.AddYears(3));
        Assert.True(cut.World.Living![id].Crime > same.World.Living![id].Crime + 5,
            $"{cut.World.Living![id].Crime:F1} против {same.World.Living![id].Crime:F1}");
        Assert.True(cut.World.Living![id].Effects["legitimacy"] < same.World.Living![id].Effects["legitimacy"]);
        Assert.Contains(cut.World[id].Stats["crime_index"].Factors!, f => f.Label == "Деньги на полицию" && f.Value > 5);
    }

    [Fact]
    public void PatrolsAndSocialHousingWork()
    {
        var sim = NewGame();
        var same = NewGame();
        var world = sim.World;
        Politics.PoliticsActions.PlayAs(world, "IND");
        Assert.True(ProgramModel.Toggle(world, "IND", "police_patrols"));
        Assert.True(ProgramModel.Toggle(world, "IND", "social_housing"));
        Assert.Equal(-0.12, Laws.LawModel.Effect(world, "IND", "crime"), 6);
        sim.RunUntil(world.Date.AddYears(5));
        same.RunUntil(same.World.Date.AddYears(5));
        Assert.True(world.Living!["IND"].Crime < same.World.Living!["IND"].Crime - 2);
        Assert.True(world.Living!["IND"].Housing > same.World.Living!["IND"].Housing * 1.03);
        Assert.True(world["IND"].Stat("housing_price_index") < same.World["IND"].Stat("housing_price_index"));
    }

    [Fact]
    public void StrictEnvironmentalStandardsCleanTheAirAndLengthenLives()
    {
        var sim = NewGame();
        var same = NewGame();
        var world = sim.World;
        var laws = world.Laws!["IND"];
        Assert.Equal("medium", laws.Current["environmental_standards"]);
        laws.Current["environmental_standards"] = "strict";
        Laws.LawModel.Recompute(world.Data, laws, world.Date);
        sim.RunUntil(world.Date.AddYears(3));
        same.RunUntil(same.World.Date.AddYears(3));
        var (a, b) = (world.Living!["IND"], same.World.Living!["IND"]);
        Assert.True(a.Pollution < b.Pollution * 0.8, $"{a.Pollution:F1} против {b.Pollution:F1}");
        Assert.True(a.Effects["life_expectancy"] > b.Effects["life_expectancy"] + 0.3);
        // Специалисты и служащие — горожане: чистый воздух радует их сильнее.
        Assert.True(a.Mood[Society.ClassIds.Professionals] - b.Mood[Society.ClassIds.Professionals]
                    > a.Mood[Society.ClassIds.Farmers] - b.Mood[Society.ClassIds.Farmers]);
    }

    [Fact]
    public void UnderpaidOfficialsTakeBribes()
    {
        var cut = NewGame();
        var same = NewGame();
        const string id = "MEX";
        cut.World.Economy![id].SpendingPlan[Item("administration")] *= 0.5;
        cut.RunUntil(cut.World.Date.AddYears(1));
        same.RunUntil(same.World.Date.AddYears(1));
        var (a, b) = (cut.World[id], same.World[id]);
        Assert.True(a.Stat("corruption_index") > b.Stat("corruption_index") + 4);
        Assert.Contains(a.Stats["corruption_index"].Factors!, f => f.Label == "Зарплаты чиновников" && f.Value > 4);
        // Коррупция бьёт по сбору налогов и росту.
        Assert.True(cut.World.Living![id].Effects["revenue.vat"] < same.World.Living![id].Effects["revenue.vat"]);
        Assert.True(cut.World.Living![id].Effects["potential_growth"] < same.World.Living![id].Effects["potential_growth"]);
    }
}
