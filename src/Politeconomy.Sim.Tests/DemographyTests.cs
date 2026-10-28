using Politeconomy.Sim.Demography;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Tests;

public class DemographyTests
{
    static World Setup()
    {
        var world = WorldFactory.Create(TestData.Game);
        DemographyModel.Setup(world);
        return world;
    }

    [Theory]
    [InlineData("RUS")]
    [InlineData("NGA")]
    [InlineData("JPN")]
    public void StartMatchesData(string id)
    {
        var world = Setup();
        var d = world.Demography![id];
        var data = world[id].Info.Stats;
        Assert.Equal(data["population"].Value, d.Total, data["population"].Value * 1e-6);
        Assert.Equal(data["age_65_plus_pct"].Value, d.Elderly / d.Total * 100, 1.5);
        Assert.Equal(data["age_0_14_pct"].Value, d.Children / d.Total * 100, 1.5);
    }

    [Fact]
    public void CountriesWithoutAgeDataBorrowStructureFromNeighbours()
    {
        // У Тайваня нет данных Всемирного банка: структура взята у Южной Кореи, население — своё.
        var world = Setup();
        var d = world.Demography!["TWN"];
        Assert.Equal(world["TWN"].Info.Stats["population"].Value, d.Total, 1);
        Assert.InRange(d.Elderly / d.Total * 100, 15, 25);
    }

    [Theory]
    [InlineData(55, 60)]
    [InlineData(72, 15)]
    [InlineData(84, 2)]
    public void MortalityScheduleGivesRequestedLifeExpectancy(double e0, double infant)
    {
        var rates = MortalityModel.Rates(e0, infant, TestData.Game.Demography);
        // Продолжительность жизни по коэффициентам пятилетних групп (таблица смертности).
        double alive = 1, years = 0;
        for (var g = 0; g < rates.Length - 1; g++)
        {
            var q = 5 * rates[g] / (1 + 2.5 * rates[g]);
            years += 5 * alive * (1 - q / 2);
            alive *= 1 - q;
        }
        years += alive / rates[^1];
        Assert.Equal(e0, years, 1.0);
        Assert.True(rates[0] > rates[2], "дети до 5 лет умирают чаще подростков");
        Assert.True(rates[^1] > rates[10], "в 80+ смертность выше, чем в 50–54");
    }

    [Fact]
    public void BirthAndDeathRatesLookLikeData()
    {
        var world = Setup();
        double Rate(string id, Func<CountryDemography, double[]> history) =>
            world.Demography![id].YearSum(history(world.Demography![id])) / world.Demography![id].Total * 1000;
        Assert.InRange(Rate("NGA", d => d.BirthsHistory), 28, 45);
        Assert.InRange(Rate("JPN", d => d.BirthsHistory), 4, 9);
        Assert.InRange(Rate("JPN", d => d.DeathsHistory), 10, 17);
        Assert.InRange(Rate("NGA", d => d.DeathsHistory), 7, 16);
    }

    [Fact]
    public void LargestCountriesFollowUnProjectionTo2050()
    {
        var sim = GameSetup.NewGame(TestData.Game).Simulation;
        var world = sim.World;
        var top = world.Countries.OrderByDescending(c => c.Stat("population")).Take(10).Select(c => c.Id).ToList();
        var medianAge = world.Demography!["JPN"].MedianAge();
        sim.RunUntil(new DateOnly(2050, 1, 1));
        var benchmarks = TestData.Game.Benchmarks!.Population;
        foreach (var id in top)
        {
            var un = benchmarks[id]["2050"];
            var model = world.Demography![id].Total;
            Assert.True(Math.Abs(model / un - 1) <= 0.15, $"{id}: модель {model / 1e6:F0} млн, ООН {un / 1e6:F0} млн");
        }
        Assert.True(world.Demography!["JPN"].MedianAge() > medianAge, "Япония стареет");
        Assert.Equal(world.Demography!["USA"].Total, world.Economy!["USA"].Population, 1e-3 * world.Economy!["USA"].Population);
    }
}
