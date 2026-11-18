using Politeconomy.Sim.Ai;
using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim.Tests;

public class AiTests
{
    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 5).Simulation;

    [Fact]
    public void EveryGovernmentDecidesAndExplains()
    {
        var sim = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "DEU");
        var laws = world.Laws!["DEU"].Current.ToDictionary(kv => kv.Key, kv => kv.Value);
        sim.RunUntil(world.Date.AddYears(3));
        var ai = world.Ai!;
        Assert.All(ai.Countries.Where(kv => kv.Key != "DEU"), kv =>
        {
            Assert.NotEmpty(kv.Value.Decisions);
            Assert.Equal(1, kv.Value.Priorities.Sum(), 9);
        });
        Assert.All(ai.Countries.Values.SelectMany(c => c.Decisions), d => Assert.False(string.IsNullOrWhiteSpace(d.Why), d.Text));
        Assert.True(ai.Counts["budget"] > 500 && ai.Counts["law"] > 10 && ai.Counts["diplomacy"] > 500, string.Join(", ", ai.Counts));
        // Страной игрока ИИ не правит и договоров ей сам не навязывает.
        Assert.Empty(ai["DEU"].Decisions);
        Assert.Equal(laws, world.Laws!["DEU"].Current);
        Assert.DoesNotContain(world.Diplomacy!.Treaties, t => t.Involves("DEU") && t.Since > TestData.Game.Scenario.StartDate);
        // Законы ИИ проходят через парламент, как у игрока.
        Assert.Contains(world.Countries, c => c.Id != "DEU" && world.Laws![c.Id].Finished.Count > 0);
    }

    [Fact]
    public void BudgetLeansTowardsPrioritiesWithinLimits()
    {
        var world = NewGame().World;
        var country = world["FRA"];
        var ai = world.Ai!["FRA"];
        var e = world.Economy!["FRA"];
        var before = e.SpendingPlan.Sum();
        var start = e.SpendingPlan.Select(x => x / before).ToArray();
        ai.Priorities = [0.1, 0.1, 0.7, 0.1];
        AiBudget.Plan(new AiTurn(world, country, ai));
        var items = TestData.Game.Budget.Spending.ToList();
        int K(string id) => items.FindIndex(x => x.Id == id);
        var shares = e.SpendingPlan.Select(x => x / e.SpendingPlan.Sum()).ToArray();
        var tilt = TestData.Game.AiBalance.Tilt;
        Assert.True(shares[K("defense")] > start[K("defense")] * 1.05, "сила — больше на оборону");
        Assert.True(shares[K("education")] < start[K("education")], "рост не в приоритете — меньше на образование");
        Assert.All(shares.Select((s, k) => (s, k)).Where(x => start[x.k] > 0),
            x => Assert.InRange(x.s / start[x.k], (1 - tilt) * 0.9, (1 + tilt) * 1.1));
        if (!AiModel.Assess(world, country).ElectionSoon && !AiModel.Assess(world, country).DebtCrisis(TestData.Game.AiBalance))
            Assert.Equal(before, e.SpendingPlan.Sum(), 9);
        Assert.Contains("оборона", ai.Decisions[^1].Text);
    }

    [Fact]
    public void SwornEnemiesGetSanctionedAndForgivenLater()
    {
        var sim = NewGame();
        var world = sim.World;
        var d = world.Diplomacy!;
        d.Memory[WorldDiplomacy.Key("ARG", "CHL")] = -150;
        world.Ai!["ARG"].Priorities = [0.1, 0.2, 0.6, 0.1];
        world.Ai!["ARG"].Last.Remove("sanction");
        sim.RunUntil(world.Date.AddMonths(2));
        Assert.NotNull(DiplomacyModel.SanctionLevel(world, "ARG", "CHL"));
        Assert.Contains(world.Ai!["ARG"].Decisions, x => x.Why.Contains("заклятый соперник"));

        d.Memory[WorldDiplomacy.Key("ARG", "CHL")] = 150;
        world.Ai!["ARG"].Last.Remove("sanction");
        sim.RunUntil(world.Date.AddMonths(2));
        Assert.Null(DiplomacyModel.SanctionLevel(world, "ARG", "CHL"));
        Assert.Contains(world.Ai!["ARG"].Decisions, x => x.Why.Contains("вражда утихла"));
    }

    [Fact]
    public void CrisisRaisesStabilityAndElectionsRaiseReelection()
    {
        var world = NewGame().World;
        var country = world["BRA"];
        var ai = world.Ai!["BRA"];
        AiModel.UpdatePriorities(world, country, ai);
        var calm = ai.Stability;
        country.Stats["inflation_pct"] = country.Stats["inflation_pct"] with { Value = 80 };
        AiModel.UpdatePriorities(world, country, ai);
        Assert.True(ai.Stability > calm + 0.05);
        Assert.Contains(ai.Why, w => w.Contains("инфляция 80%"));
        var p = world.Politics!["BRA"];
        (p.NextElection, p.NextPresident) = (world.Date.AddYears(3), world.Date.AddYears(3));
        AiModel.UpdatePriorities(world, country, ai);
        var before = ai.Reelection;
        p.NextElection = world.Date.AddMonths(3);
        AiModel.UpdatePriorities(world, country, ai);
        Assert.True(ai.Reelection > before);
        Assert.Contains("скоро выборы", ai.Why);
    }

    [Fact]
    public void SameStartSameWorldAndSavingKeepsTheAiOnCourse()
    {
        var a = NewGame();
        var b = NewGame();
        a.RunUntil(a.World.Date.AddMonths(14));
        b.RunUntil(b.World.Date.AddMonths(14));
        Assert.Equal(SaveGame.Hash(a.World), SaveGame.Hash(b.World));

        using var stream = new MemoryStream();
        SaveGame.Write(a.World, stream);
        stream.Position = 0;
        var loaded = GameSetup.Resume(SaveGame.Read(TestData.Game, stream));
        a.RunUntil(a.World.Date.AddMonths(13));
        loaded.RunUntil(a.World.Date);
        Assert.Equal(SaveGame.Hash(a.World), SaveGame.Hash(loaded.World));
        Assert.Equal(a.World.Ai!.Counts, loaded.World.Ai!.Counts);
    }
}
