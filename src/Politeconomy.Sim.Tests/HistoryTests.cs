using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Save;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Tests;

public class HistoryTests
{
    [Fact]
    public void HistoryStartsFromDataAndGrowsEachJanuary()
    {
        var sim = GameSetup.NewGame(TestData.Game, 3).Simulation;
        var h = sim.World.History!;
        Assert.Equal(TestData.Game.History.FirstYear, h.FirstYear);
        Assert.Equal(TestData.Game.History.LastYear, h.LastYear);
        var length = h.LastYear - h.FirstYear + 1;
        var bra = h.Values["BRA"];
        Assert.All(bra.Values, row => Assert.Equal(length, row.Count));
        Assert.True(bra["gdp_growth_pct"].Count(v => v != null) >= length - 2);
        // Индексов институтов в данных нет: их история начинается с первого года игры.
        Assert.All(bra["approval_pct"], v => Assert.Null(v));

        var year = sim.World.Date.Year;
        sim.RunUntil(new DateOnly(year + 1, 1, 2));
        Assert.Equal(year, h.LastYear);
        Assert.Equal(length + 1, bra["gdp_growth_pct"].Count);
        Assert.Equal(sim.World.Countries.First(c => c.Id == "BRA").Stats["approval_pct"].Value, bra["approval_pct"][^1]!.Value, 3);
    }

    [Fact]
    public void LastYearOfDataIsCloseToTheStart()
    {
        // История из данных и стартовые показатели модели — одна линия, без скачка на последнем отрезке графика.
        var world = GameSetup.NewGame(TestData.Game, 3).Simulation.World;
        foreach (var id in new[] { "USA", "CHN", "DEU", "BRA", "IND", "NGA" })
        {
            var country = world.Countries.First(c => c.Id == id);
            foreach (var stat in new[] { "population", "gdp_per_capita_usd", "life_expectancy" })
            {
                // Последний год данных бывает пуст (статистика выходит с задержкой) — берём последнее известное значение.
                var last = world.History!.Values[id][stat].Last(v => v != null)!.Value;
                var ratio = country.Stats[stat].Value / last;
                Assert.InRange(ratio, 0.85, 1.2);
            }
        }
    }

    [Fact]
    public void HistoryIsSavedAndLoaded()
    {
        var sim = GameSetup.NewGame(TestData.Game, 3).Simulation;
        sim.RunUntil(new DateOnly(sim.World.Date.Year + 1, 1, 2));
        using var stream = new MemoryStream();
        SaveGame.Write(sim.World, stream);
        stream.Position = 0;
        var loaded = SaveGame.Read(TestData.Game, stream);
        Assert.Equal(sim.World.History!.LastYear, loaded.History!.LastYear);
        Assert.Equal(sim.World.History.Values["JPN"]["inflation_pct"], loaded.History.Values["JPN"]["inflation_pct"]);
        Assert.Equal(SaveGame.Hash(sim.World), SaveGame.Hash(loaded));
    }

    [Fact]
    public void EveryMinistryHasAReport()
    {
        var world = GameSetup.NewGame(TestData.Game, 3).Simulation.World;
        var snapshot = WorldSnapshot.Capture(world, 1, true, 1);
        var bra = snapshot.Countries["BRA"];
        Assert.Equal(TestData.Game.Budget.Spending.Count, bra.Spending!.Count);
        Assert.NotEmpty(bra.History!.Series("gdp_growth_pct"));
        foreach (var post in TestData.Game.Government.Posts)
        {
            Assert.InRange(post.Stats!.Count, 1, 4);
            var lines = MinistryReport.Lines(TestData.Game, post, bra, snapshot, bra.Government!.Minister(post.Id));
            Assert.True(lines.Count > post.Stats.Count, post.Id);
            Assert.All(lines, l => Assert.DoesNotContain("NaN", l));
        }
        var economy = MinistryReport.Lines(TestData.Game, TestData.Game.Government.Posts.First(p => p.Id == "economy"), bra, snapshot, null);
        Assert.Contains(economy, l => l.Contains($"в {MinistryReport.ReferenceYear(snapshot.Date)} году") && l.Contains("у типичной страны мира"));
    }
}
