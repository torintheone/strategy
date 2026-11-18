using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim.Tests;

public class GovernmentTests
{
    static World NewWorld(ulong seed = 3) => GameSetup.NewGame(TestData.Game, seed).Simulation.World;

    [Fact]
    public void EveryCountryHasLeaderAndEighteenMinisters()
    {
        var world = NewWorld();
        foreach (var country in world.Countries)
        {
            var g = world.Government![country.Id];
            Assert.False(string.IsNullOrWhiteSpace(g.Leader.Name), country.Id);
            Assert.Equal(18, g.Ministers.Count);
            Assert.All(g.Ministers.Values, m => Assert.InRange(m.Competence, 1, 99));
        }
        var ids = world.Government!.Countries.Values.SelectMany(g => g.Ministers.Values.Append(g.Leader)).Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ParliamentDependsOnSystemAndRegime()
    {
        var world = NewWorld();
        Parliament P(string id) => world.Government![id].Parliament;
        Assert.Equal(450, P("RUS").Seats);
        Assert.Equal("formal", P("RUS").Kind);
        Assert.Equal("formal", P("CHN").Kind);
        Assert.Equal("none", P("MLI").Kind);
        Assert.Equal("real", P("DEU").Kind);
        Assert.True(P("DEU").RulingSeats > P("DEU").Seats / 2, "в парламентской республике у правительства большинство");
        Assert.True(P("RUS").RulingSeats > 0.8 * P("RUS").Seats);
        Assert.InRange(P("FRA").Seats, 500, 600);
    }

    [Theory]
    [InlineData("east_slavic", "Иванов", "Иванова")]
    [InlineData("east_slavic", "Высоцкий", "Высоцкая")]
    [InlineData("east_slavic", "Толстой", "Толстая")]
    [InlineData("ukrainian", "Шевченко", "Шевченко")]
    [InlineData("polish", "Ковальский", "Ковальская")]
    [InlineData("czech_slovak", "Новак", "Новакова")]
    [InlineData("greek", "Пападопулос", "Пападопулу")]
    [InlineData("baltic", "Казлаускас", "Казлаускене")]
    [InlineData("baltic", "Берзиньш", "Берзиня")]
    [InlineData("anglo", "Смит", "Смит")]
    public void FemaleSurnamesFollowLanguage(string culture, string male, string female)
    {
        var c = TestData.Game.Cultures.First(x => x.Id == culture);
        Assert.Equal(female, CharacterFactory.Feminine(c, male));
    }

    [Fact]
    public void NamesComeFromCountryCulture()
    {
        var world = NewWorld();
        var japanese = TestData.Game.Cultures.First(c => c.Id == "japanese");
        var leader = world.Government!["JPN"].Leader;
        Assert.Contains(japanese.Surnames, s => leader.Name.EndsWith(s, StringComparison.Ordinal));
        var chinese = TestData.Game.Cultures.First(c => c.Id == "chinese");
        Assert.Contains(chinese.Surnames, s => world.Government!["CHN"].Leader.Name.StartsWith(s + " ", StringComparison.Ordinal));
    }

    [Fact]
    public void ParliamentVotesFollowMajorityAndPopularity()
    {
        var world = NewWorld();
        var democracy = Parliament.Blocks("real", "", 400, 220, 0.82);
        Assert.True(ParliamentVote.Cast(world, democracy, 0.5).Passed, "правящее большинство проводит обычный проект");
        var divided = Parliament.Blocks("real", "", 400, 170, 0.82);
        Assert.False(ParliamentVote.Cast(world, divided, 0.3).Passed, "без большинства непопулярный проект проваливается");
        Assert.True(ParliamentVote.Cast(world, divided, 0.9).Passed, "популярный проект поддерживает и оппозиция");
        Assert.False(ParliamentVote.Cast(world, democracy, 0.5, constitutional: true).Passed, "для конституции нужно две трети");
        var formal = Parliament.Blocks("formal", "", 400, 370, 0.99);
        Assert.True(ParliamentVote.Cast(world, formal, 0.05).Passed, "формальный парламент одобряет всё");
        var none = new Parliament { Kind = "none" };
        Assert.True(ParliamentVote.Cast(world, none, 0.1).Decree);
    }

    [Fact]
    public void PlayerCanReplaceMinister()
    {
        var world = NewWorld();
        var old = world.Government!["BRA"].Ministers["finance"];
        GovernmentActions.RequestCandidates(world, "BRA", "finance");
        var candidates = world.Government!["BRA"].Candidates["finance"];
        Assert.Equal(3, candidates.Count);
        Assert.True(GovernmentActions.Appoint(world, "BRA", "finance", 1));
        var now = world.Government!["BRA"].Ministers["finance"];
        Assert.Same(candidates[1], now);
        Assert.NotSame(old, now);
        Assert.False(world.Government!["BRA"].Candidates.ContainsKey("finance"));
        Assert.Contains(world.Journal, e => e.Country == "BRA" && e.Kind == "appointment" && e.Text.Contains(now.Name));
    }

    [Fact]
    public void OldMinistersRetireAndAreReplaced()
    {
        var sim = GameSetup.NewGame(TestData.Game, 9).Simulation;
        sim.RunUntil(sim.World.Date.AddYears(15));
        var world = sim.World;
        Assert.Contains(world.Journal, e => e.Kind == "retirement");
        var oldest = world.Government!.Countries.Values.SelectMany(g => g.Ministers.Values).Max(m => m.Age(world.Date));
        Assert.True(oldest < 92, $"самому старому министру {oldest}");
    }

    [Fact]
    public void GovernmentSurvivesSaveAndLoad()
    {
        var world = NewWorld();
        GovernmentActions.RequestCandidates(world, "RUS", "health");
        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = SaveGame.Read(TestData.Game, stream);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
        Assert.Equal(world.Government!["RUS"].Leader.Name, loaded.Government!["RUS"].Leader.Name);
        Assert.Equal(3, loaded.Government!["RUS"].Candidates["health"].Count);
        Assert.Equal(Regime.Authoritarian, loaded["RUS"].Regime);
    }
}
