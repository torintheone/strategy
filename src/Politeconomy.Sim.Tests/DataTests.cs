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
    public void AllUnMembersAndOtherStatesAreLoaded()
    {
        var data = TestData.Game;
        Assert.Equal(198, data.Countries.Countries.Count);
        Assert.Equal(193, data.Countries.Countries.Count(c => c.UnMember));
        Assert.Equal(198, data.Map.Countries.Count);
        Assert.Equal(198, data.Politics.Countries.Count);
        Assert.Contains(data.Countries.Countries, c => c.Id == "RUS" && c.Name == "Россия");
        foreach (var id in new[] { "TWN", "XKX", "PSE", "SOL", "CYN" })
        {
            var c = data.Countries.Countries.Single(x => x.Id == id);
            Assert.False(c.UnMember);
            Assert.False(string.IsNullOrEmpty(c.StatusNote));
        }
    }

    [Fact]
    public void DebtComesFromImfForMajorEconomies()
    {
        var data = TestData.Game;
        foreach (var id in new[] { "USA", "CHN", "DEU", "JPN", "RUS", "IND", "BRA", "TWN" })
        {
            var debt = data.Countries.Countries.Single(c => c.Id == id).Stats["gov_debt_pct_gdp"];
            Assert.StartsWith("imf:", debt.Source);
        }
    }

    [Fact]
    public void EveryCountryHasRegionsAndACapital()
    {
        var data = TestData.Game;
        var regions = data.Regions.Regions.GroupBy(r => r.Country).ToDictionary(g => g.Key, g => g.ToList());
        var capitals = data.Cities.Cities.Where(c => c.Capital).ToDictionary(c => c.Country, c => c.Name);
        foreach (var c in data.Countries.Countries)
        {
            Assert.True(regions.ContainsKey(c.Id), $"у {c.Id} нет регионов");
            Assert.True(capitals.ContainsKey(c.Id), $"у {c.Id} нет столицы");
        }
        Assert.Equal("Москва", capitals["RUS"]);
        Assert.Equal("Тайбэй", capitals["TWN"]);
        Assert.Equal("Иерусалим", capitals["ISR"]);
        Assert.Equal("Рамалла", capitals["PSE"]);
        Assert.Contains(regions["MDA"], r => r.Name == "Приднестровье");
        Assert.Contains(regions["UKR"], r => r.Name == "Севастополь");
        Assert.True(regions["RUS"].Count > 80);
        Assert.Single(regions["SGP"]);
    }

    [Fact]
    public void WorldStartsFromScenarioDate()
    {
        var world = WorldFactory.Create(TestData.Game);
        Assert.Equal(new DateOnly(2026, 1, 1), world.Date);
        Assert.Equal(198, world.Countries.Count);
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
        Regions = d.Regions,
        Control = d.Control,
        Politics = politics,
        Scenario = d.Scenario,
        Balance = d.Balance,
        Indicators = d.Indicators,
        MapModes = d.MapModes,
        Map = d.Map,
        Cities = d.Cities,
        Currencies = d.Currencies,
        Industries = d.Industries,
        Budget = d.Budget,
        Population = d.Population,
        Classes = d.Classes,
        Government = d.Government,
        Cultures = d.Cultures,
        Benchmarks = d.Benchmarks,
    };
}
