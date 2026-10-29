using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Tests;

public class ProtestTests
{
    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 3).Simulation;

    static Protest Force(World world, string country, int k, int stage, string cause = "mood") =>
        ProtestModel.Start(world, world[country], world.Politics![country], k, cause, stage);

    [Fact]
    public void CalmStartHasNoProtests()
    {
        var world = NewGame().World;
        Assert.All(world.Politics!.Countries.Values, p => Assert.Empty(p.Protests));
        Assert.All(world.Countries, c => Assert.Equal(0, c.Stat("unrest_index")));
        Assert.All(world.Politics!.Countries.Values, p => Assert.InRange(p.Security.PoliceLoyalty, 20, 80));
    }

    [Fact]
    public void RadicalOrganizedClassTakesToTheStreets()
    {
        var sim = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "FRA");
        var s = world.Society!["FRA"];
        for (var month = 0; month < 18 && world.Politics!["FRA"].Protests.All(x => x.Class != ClassIds.Skilled); month++)
        {
            s.Radicalism[ClassIds.Skilled] = 60;
            sim.RunUntil(world.Date.AddMonths(1));
        }
        Assert.Contains(world.Journal, e => e.Country == "FRA" && e.Kind == "protest" && e.Text.StartsWith("Квалифицированные рабочие"));
        Assert.True(world["FRA"].Stat("unrest_index") > 0 || world.Politics!["FRA"].Protests.Count == 0);
    }

    [Fact]
    public void HatedLawSparksProtestAndConcessionRepealsIt()
    {
        var sim = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "FRA");
        var laws = world.Laws!["FRA"];
        var law = TestData.Game.Law("retirement_age");
        var from = laws.Current[law.Id];
        laws.Current[law.Id] = "a67";
        Laws.LawModel.Recompute(world.Data, laws, world.Date);
        var s = world.Society!["FRA"];
        for (var k = 0; k < s.Organization.Length; k++)
            s.Organization[k] = 1;
        ProtestModel.LawPassed(world, "FRA", law, from, "a67");
        var protest = Assert.Single(world.Politics!["FRA"].Protests.Where(x => x.Cause == "law").Take(1));
        Assert.Equal(law.Id, protest.Law);
        Assert.Equal(from, protest.Variant);
        Assert.True(protest.Stage >= 1);

        Assert.True(PoliticsActions.RespondToProtest(world, "FRA", protest.Id, "concede"));
        Assert.Equal(from, laws.Current[law.Id]);
        Assert.DoesNotContain(protest, world.Politics!["FRA"].Protests);
        Assert.False(PoliticsActions.RespondToProtest(world, "FRA", protest.Id, "concede"), "протеста больше нет");
    }

    [Fact]
    public void DispersalHasAPrice()
    {
        var sim = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "BRA");
        var p = world.Politics!["BRA"];
        var s = world.Society!["BRA"];
        var protest = Force(world, "BRA", ClassIds.Unskilled, stage: 2);
        var radicalism = s.Radicalism[ClassIds.Unskilled];
        var loyalty = p.Security.PoliceLoyalty;
        Assert.True(PoliticsActions.RespondToProtest(world, "BRA", protest.Id, "disperse"));
        Assert.False(PoliticsActions.RespondToProtest(world, "BRA", protest.Id, "negotiate"), "не чаще раза в месяц");
        Assert.True(s.Radicalism[ClassIds.Unskilled] > radicalism);
        Assert.True(p.Security.Repression > 0);
        Assert.True(p.Security.PoliceLoyalty < loyalty);
        Assert.True(p.ProtestEffects.GetValueOrDefault("index.civil_liberties") < 0);
        Assert.Contains(world.Journal, e => e.Country == "BRA" && e.Kind == "protest" && e.Text.Contains("разогна") || e.Text.Contains("не справилась"));
    }

    [Fact]
    public void MassUnrestHurtsTheEconomyAndLegitimacy()
    {
        var sim = NewGame();
        var calm = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "DEU");
        for (var month = 0; month < 4; month++)
        {
            var protest = world.Politics!["DEU"].Protests.FirstOrDefault(x => x.Class == ClassIds.Unskilled) ?? Force(world, "DEU", ClassIds.Unskilled, stage: 3);
            protest.Stage = 3;
            protest.Heat = 3;
            sim.RunUntil(world.Date.AddMonths(1));
            calm.RunUntil(calm.World.Date.AddMonths(1));
        }
        Assert.True(world["DEU"].Stat("unrest_index") > 20, $"беспорядки {world["DEU"].Stat("unrest_index"):F1}, протестов {world.Politics!["DEU"].Protests.Count}, стадии {string.Join(",", world.Politics!["DEU"].Protests.Select(x => x.Stage))}");
        Assert.True(world["DEU"].Stat("legitimacy") < calm.World["DEU"].Stat("legitimacy") - 3, $"легитимность {world["DEU"].Stat("legitimacy"):F1} против {calm.World["DEU"].Stat("legitimacy"):F1}, поправка {world.Politics!["DEU"].ProtestEffects.GetValueOrDefault("legitimacy"):F1}");
        Assert.True(world.Economy!["DEU"].GapCore < calm.World.Economy!["DEU"].GapCore - 0.005);
    }

    [Fact]
    public void ArrestsWeakenTheOppositionAtACost()
    {
        var sim = NewGame();
        var calm = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "BRA");
        var p = world.Politics!["BRA"];
        var target = ProtestModel.Target(p)!;
        var (leader, organization) = (target.Leader!.Name, target.Organization);
        Assert.True(PoliticsActions.ArrestOpposition(world, "BRA"));
        Assert.False(PoliticsActions.ArrestOpposition(world, "BRA"), "не чаще раза в год");
        Assert.NotEqual(leader, target.Leader!.Name);
        Assert.True(target.Organization < organization);
        Assert.True(p.Security.Repression >= 25);
        Assert.Contains(world.Journal, e => e.Country == "BRA" && e.Kind == "repression");
        sim.RunUntil(world.Date.AddMonths(2));
        calm.RunUntil(calm.World.Date.AddMonths(2));
        Assert.True(world["BRA"].Stat("civil_liberties_index") < calm.World["BRA"].Stat("civil_liberties_index") - 2);
        Assert.True(world.Politics!["BRA"].Groups.Single(g => g.Id == "intelligentsia").FromRepression < -3);
    }

    [Fact]
    public void ExposedFraudBringsMassProtests()
    {
        var world = NewGame().World;
        ProtestModel.FraudExposed(world, world["BRA"], world.Politics!["BRA"]);
        Assert.Contains(world.Politics!["BRA"].Protests, x => x.Cause == "fraud" && x.Stage >= 2);
    }

    [Fact]
    public void ComputerGovernmentsAnswerProtests()
    {
        var sim = NewGame();
        var world = sim.World;
        var protest = Force(world, "RUS", ClassIds.Professionals, stage: 2);
        sim.RunUntil(world.Date.AddMonths(1).AddDays(1));
        Assert.True(protest.Response != null || !world.Politics!["RUS"].Protests.Contains(protest));
    }

    [Fact]
    public void ProtestsSurviveSaveAndLoad()
    {
        var sim = NewGame();
        var world = sim.World;
        Force(world, "FRA", ClassIds.Skilled, stage: 1);
        PoliticsActions.PlayAs(world, "BRA");
        PoliticsActions.ArrestOpposition(world, "BRA");
        sim.RunUntil(world.Date.AddMonths(2));
        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = SaveGame.Read(TestData.Game, stream);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
        var resumed = GameSetup.Resume(loaded);
        sim.RunUntil(world.Date.AddMonths(3));
        resumed.RunUntil(world.Date);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(resumed.World));
    }
}
