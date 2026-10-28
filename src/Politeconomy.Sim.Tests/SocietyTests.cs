using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;
using Politeconomy.Sim.Society.Systems;

namespace Politeconomy.Sim.Tests;

public class SocietyTests
{
    static readonly Lazy<World> Start = new(() => GameSetup.NewGame(TestData.Game, 3).Simulation.World);

    [Fact]
    public void ClassesAddUpToPopulation()
    {
        var world = Start.Value;
        foreach (var country in world.Countries)
        {
            var s = world.Society![country.Id];
            Assert.Equal(world.Demography![country.Id].Total, s.Total, s.Total * 1e-6);
            Assert.All(s.Size, x => Assert.True(x >= 0, country.Id));
            Assert.Equal(100, s.Influence.Sum(), 1e-6);
        }
    }

    [Theory]
    [InlineData("USA")]
    [InlineData("IND")]
    [InlineData("DEU")]
    public void ConsumptionMatchesNationalAccounts(string id)
    {
        var world = Start.Value;
        var s = world.Society![id];
        var e = world.Economy![id];
        var consumption = Enumerable.Range(0, ClassIds.Count).Sum(k => s.Consumption[k] * s.Size[k]);
        var data = world[id].Info.Stats["consumption_private_pct_gdp"].Value / 100 * SocietyModel.PppGdp(e);
        Assert.Equal(data, consumption, data * 0.02);
    }

    [Fact]
    public void RicherClassesLiveBetter()
    {
        var world = Start.Value;
        foreach (var country in world.Countries)
        {
            var s = world.Society![country.Id];
            Assert.True(s.StandardOfLiving[ClassIds.Capitalists] >= s.StandardOfLiving[ClassIds.Unskilled], country.Id);
            Assert.True(s.Income[ClassIds.Professionals] > s.Income[ClassIds.Unskilled], $"{country.Id}: специалисты богаче рабочих");
            Assert.True(s.Income[ClassIds.Unemployed] < s.Income[ClassIds.Clerks], $"{country.Id}: безработные беднее служащих");
        }
    }

    [Fact]
    public void FarmersAreManyInPoorCountriesAndFewInRichOnes()
    {
        var world = Start.Value;
        double Share(string id, int k) => world.Society![id].Size[k] / world.Society![id].Total;
        Assert.True(Share("ETH", ClassIds.Farmers) > 0.3);
        Assert.True(Share("DEU", ClassIds.Farmers) < 0.03);
        Assert.True(Share("DEU", ClassIds.Professionals) > Share("ETH", ClassIds.Professionals) * 3);
        Assert.True(Share("JPN", ClassIds.Pensioners) > Share("NGA", ClassIds.Pensioners) * 3);
    }

    [Fact]
    public void StandardOfLivingGrowsWithConsumptionAndFoodPricesHurt()
    {
        var b = TestData.Game.Society;
        Assert.Equal(0, ClassesSystem.StandardOfLiving(0, 1, b), 6);
        Assert.Equal(100, ClassesSystem.StandardOfLiving(1e9, 1, b), 6);
        var levels = new[] { 500, 2000, 5000, 15000, 40000, 120000 }.Select(c => ClassesSystem.StandardOfLiving(c, 1, b)).ToList();
        Assert.True(levels.Zip(levels.Skip(1)).All(t => t.Second > t.First));
        Assert.True(ClassesSystem.StandardOfLiving(1500, 1.5, b) < ClassesSystem.StandardOfLiving(1500, 1, b));
    }

    /// <summary>Два одинаковых мира: в первом что-то меняем, оба живут несколько месяцев с одинаковыми толчками.</summary>
    static (World Changed, World Base) Compare(Action<World> change, int months)
    {
        var a = GameSetup.NewGame(TestData.Game, 3).Simulation;
        var b = GameSetup.NewGame(TestData.Game, 3).Simulation;
        change(a.World);
        a.RunUntil(a.World.Date.AddMonths(months));
        b.RunUntil(b.World.Date.AddMonths(months));
        return (a.World, b.World);
    }

    [Fact]
    public void RecessionMakesPeopleUnhappyAndPushesWorkersIntoUnemployment()
    {
        var (crisis, baseline) = Compare(w => w.Economy!["BRA"].GapCore = -0.15, months: 9);
        var x = crisis.Society!["BRA"];
        var y = baseline.Society!["BRA"];
        Assert.True(x.Size[ClassIds.Unemployed] > y.Size[ClassIds.Unemployed] * 1.2, "безработных больше");
        Assert.True(crisis["BRA"].Stat("satisfaction") < baseline["BRA"].Stat("satisfaction") - 3, "удовлетворённость ниже");
        Assert.True(x.Satisfaction[ClassIds.Unskilled] < y.Satisfaction[ClassIds.Unskilled] - 3, "рабочие недовольны");
        Assert.True(x.Expectations[ClassIds.Unemployed] > y.Expectations[ClassIds.Unemployed],
            "новые безработные приносят ожидания прежней жизни");
    }

    [Fact]
    public void LongDiscontentBreedsRadicalism()
    {
        var (crisis, baseline) = Compare(w =>
        {
            var e = w.Economy!["ARG"];
            e.GapCore = -0.2;
            e.Inflation += 1.0;
            e.Expectations += 0.8;
        }, months: 24);
        Assert.True(crisis["ARG"].Stat("satisfaction") < 40);
        Assert.True(crisis["ARG"].Stat("radicalism") > baseline["ARG"].Stat("radicalism") + 2);
    }

    [Fact]
    public void ExpectationsCatchUpWithLivingStandards()
    {
        var sim = GameSetup.NewGame(TestData.Game, 5).Simulation;
        sim.RunUntil(sim.World.Date.AddYears(10));
        var s = sim.World.Society!["IND"];
        for (var k = 0; k < ClassIds.Count; k++)
            Assert.InRange(s.StandardOfLiving[k] - s.Expectations[k], -3, 5);
    }

    [Fact]
    public void ElectionsGiveNumbersMoreWeight()
    {
        var world = Start.Value;
        // В Индии выборы: у крестьян больше влияния, чем у госаппарата; в Китае — наоборот.
        Assert.True(world.Society!["IND"].Influence[ClassIds.Farmers] > world.Society!["IND"].Influence[ClassIds.Officials]);
        Assert.True(world.Society!["CHN"].Influence[ClassIds.Officials] > world.Society!["CHN"].Influence[ClassIds.Farmers]);
    }
}
