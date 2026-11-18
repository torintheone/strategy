using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Tests;

public class PowerTests
{
    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 3).Simulation;

    [Fact]
    public void MilitaryCoupInstallsAJunta()
    {
        var sim = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "NGA");
        var p = world.Politics!["NGA"];
        var oldLeader = world.Government!["NGA"].Leader.Name;
        PowerModel.MilitaryCoup(world, world["NGA"], p);
        var g = world.Government!["NGA"];
        Assert.Equal(PoliticalSystem.MilitaryRegime, world["NGA"].System);
        Assert.Equal(Regime.Authoritarian, world["NGA"].Regime);
        Assert.False(g.Parliament.Exists);
        Assert.True(g.Leader.Has("silovik"));
        Assert.Null(g.Leader.Party);
        Assert.DoesNotContain(p.Parties, x => x.Ruling);
        Assert.Null(p.NextElection);
        Assert.Equal("banned", world.Laws!["NGA"].Current["assembly"]);
        Assert.Equal("military_coup", world.Defeat!.Reason);
        Assert.Equal(oldLeader, world.Defeat.OldLeader);
        Assert.Contains(world.Journal, e => e.Country == "NGA" && e.Kind == "power" && e.Text.StartsWith("Военный переворот"));
        // Жизнь при хунте идёт: законы — указами, выборов нет.
        sim.RunUntil(world.Date.AddMonths(6));
        Assert.Equal(PoliticalSystem.MilitaryRegime, world["NGA"].System);
    }

    [Fact]
    public void JuntaHandsPowerBackAndElectionsFollow()
    {
        var sim = NewGame();
        var world = sim.World;
        var p = world.Politics!["NGA"];
        var system = world["NGA"].System;
        PowerModel.MilitaryCoup(world, world["NGA"], p);
        p.Junta!.HandOver = world.Date.AddMonths(1);
        sim.RunUntil(world.Date.AddMonths(1).AddDays(1));
        Assert.Equal(system, world["NGA"].System);
        Assert.Equal(Regime.Hybrid, world["NGA"].Regime);
        Assert.True(world.Government!["NGA"].Parliament.Exists);
        Assert.NotNull(p.NextElection);
        Assert.Contains(p.Changes, c => c.Kind == "handover");
        sim.RunUntil(p.NextElection!.Value.AddDays(1));
        Assert.NotNull(world.Government!["NGA"].Leader.Party);
        Assert.Contains(p.Parties, x => x.Ruling);
    }

    [Fact]
    public void RevolutionOverthrowsAnAbsoluteMonarchy()
    {
        var sim = NewGame();
        var world = sim.World;
        var p = world.Politics!["SAU"];
        Assert.Empty(p.Parties);
        PowerModel.Revolution(world, world["SAU"], p, ClassIds.Professionals);
        Assert.NotEmpty(p.Parties);
        Assert.Equal(PoliticalSystem.ParliamentaryRepublic, world["SAU"].System);
        Assert.Equal(Regime.Hybrid, world["SAU"].Regime);
        Assert.True(world.Government!["SAU"].Parliament is { Kind: "real", Exists: true });
        var head = PoliticsModel.HeadParty(world, "SAU");
        Assert.True(head is { Ruling: true });
        Assert.Equal("free", world.Laws!["SAU"].Current["assembly"]);
        Assert.NotNull(p.NextElection);
        sim.RunUntil(p.NextElection!.Value.AddMonths(1));
        Assert.Contains(p.Elections, e => e.Kind == "parliament");
        Assert.NotNull(PoliticsModel.HeadParty(world, "SAU"));
    }

    [Fact]
    public void NoConfidenceBringsDownAMinorityGovernment()
    {
        var sim = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "NLD");
        var head = PoliticsModel.HeadParty(world, "NLD")!;
        Cabinet.Form(world, world["NLD"], [head.Id], head, null);
        Assert.True(world.Government!["NLD"].Parliament.RulingSeats * 2 <= world.Government!["NLD"].Parliament.Seats);
        Assert.True(PowerModel.NoConfidence(world, world["NLD"], world.Politics!["NLD"]));
        Assert.NotEqual(head.Id, PoliticsModel.HeadParty(world, "NLD")!.Id);
        Assert.Equal("no_confidence", world.Defeat!.Reason);
    }

    [Fact]
    public void ImpeachmentReplacesThePresidentButNotTheParty()
    {
        var world = NewGame().World;
        PoliticsActions.PlayAs(world, "USA");
        var g = world.Government!["USA"];
        var party = g.Leader.Party;
        var old = g.Leader.Name;
        // Оппозиция — больше двух третей.
        var ruling = g.Parliament.Factions.Where(f => f.Ruling).ToList();
        var opposition = g.Parliament.Factions.Where(f => !f.Ruling).ToList();
        foreach (var f in ruling)
            f.Seats = 10;
        opposition[0].Seats = g.Parliament.Seats - ruling.Sum(f => f.Seats) - opposition.Skip(1).Sum(f => f.Seats);
        Assert.True(PowerModel.Impeach(world, world["USA"], world.Politics!["USA"]));
        Assert.NotEqual(old, g.Leader.Name);
        Assert.Equal(party, g.Leader.Party);
        Assert.Equal("impeachment", world.Defeat!.Reason);
    }

    [Fact]
    public void PalaceCoupKeepsTheRegime()
    {
        var world = NewGame().World;
        var g = world.Government!["RUS"];
        var party = g.Leader.Party;
        var old = g.Leader.Name;
        PowerModel.PalaceCoup(world, world["RUS"], world.Politics!["RUS"]);
        Assert.NotEqual(old, g.Leader.Name);
        Assert.Equal(party, g.Leader.Party);
        Assert.Equal(Regime.Authoritarian, world["RUS"].Regime);
        Assert.Contains(world.Politics!["RUS"].Changes, c => c.Kind == "palace_coup");
    }

    [Fact]
    public void DisloyalArmyAndUprisingRaiseTheThreats()
    {
        var world = NewGame().World;
        var p = world.Politics!["NGA"];
        var calm = PowerModel.Estimate(world, world["NGA"], p);
        p.Groups.Single(x => x.Id == "security").Approval = 5;
        p.Security.ArmyStrain = 20;
        ProtestModel.Loyalty(world, world["NGA"], p);
        world.Society!["NGA"].Radicalism[ClassIds.Unskilled] = 60;
        ProtestModel.Start(world, world["NGA"], p, ClassIds.Unskilled, "mood", stage: 4);
        var tense = PowerModel.Estimate(world, world["NGA"], p);
        Assert.True(tense.MilitaryCoup > calm.MilitaryCoup + 0.01);
        Assert.True(tense.Revolution > 0.1);
    }

    [Fact]
    public void JuntaSurvivesSaveAndLoad()
    {
        var sim = NewGame();
        var world = sim.World;
        PowerModel.MilitaryCoup(world, world["NGA"], world.Politics!["NGA"]);
        sim.RunUntil(world.Date.AddMonths(2));
        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = SaveGame.Read(TestData.Game, stream);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
        Assert.Equal(PoliticalSystem.MilitaryRegime, loaded["NGA"].System);
        Assert.NotNull(loaded.Politics!["NGA"].Junta);
        var resumed = GameSetup.Resume(loaded);
        sim.RunUntil(world.Date.AddMonths(3));
        resumed.RunUntil(world.Date);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(resumed.World));
    }
}
