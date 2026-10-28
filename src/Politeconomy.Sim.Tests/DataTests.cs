using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Tests;

public class DataTests
{
    [Fact]
    public void RealDataPassesValidation()
    {
        var problems = DataValidator.Validate(TestData.Game);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void AllUnMembersAreLoaded()
    {
        var data = TestData.Game;
        Assert.Equal(193, data.Countries.Countries.Count);
        Assert.Equal(193, data.Map.Countries.Count);
        Assert.Equal(193, data.Politics.Countries.Count);
        Assert.Contains(data.Countries.Countries, c => c.Id == "RUS" && c.Name == "Россия");
    }

    [Fact]
    public void WorldStartsFromScenarioDate()
    {
        var world = WorldFactory.Create(TestData.Game);
        Assert.Equal(new DateOnly(2026, 1, 1), world.Date);
        Assert.Equal(193, world.Countries.Count);
        Assert.True(world["USA"].Stat("population") > 300e6);
        Assert.Equal(PoliticalSystem.PresidentialRepublic, world["USA"].System);
        Assert.Equal(PoliticalSystem.ConstitutionalMonarchy, world["GBR"].System);
    }

    [Fact]
    public void ValidatorCatchesBrokenData()
    {
        var data = TestData.Game;
        var countries = data.Countries.Countries.ToList();
        var usa = countries.First(c => c.Id == "USA");
        var stats = usa.Stats.ToDictionary();
        stats["unemployment_pct"] = stats["unemployment_pct"] with { Value = 250 };
        stats.Remove("population");
        countries[countries.IndexOf(usa)] = usa with { Stats = stats, IncomeGroup = "rich" };
        var broken = Copy(data, data.Countries with { Countries = countries },
            data.Politics with { Countries = data.Politics.Countries.Append(new PoliticsRecord("XXX", "kingdom", "", false)).ToList() });

        var problems = DataValidator.Validate(broken).Select(p => p.ToString()).ToList();

        Assert.Contains(problems, p => p.Contains("USA.unemployment_pct"));
        Assert.Contains(problems, p => p.Contains("нет показателя population"));
        Assert.Contains(problems, p => p.Contains("rich"));
        Assert.Contains(problems, p => p.Contains("XXX"));
        Assert.Contains(problems, p => p.Contains("kingdom"));
    }

    static GameData Copy(GameData d, CountriesFile countries, PoliticsFile politics) => new()
    {
        Directory = d.Directory,
        Countries = countries,
        Politics = politics,
        Scenario = d.Scenario,
        Balance = d.Balance,
        Indicators = d.Indicators,
        MapModes = d.MapModes,
        Map = d.Map,
        Cities = d.Cities,
    };
}
