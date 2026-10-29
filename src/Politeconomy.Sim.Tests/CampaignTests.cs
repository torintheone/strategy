using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim.Tests;

public class CampaignTests
{
    const string Id = "BRA";
    static readonly DateOnly Election = new(2026, 10, 1);

    static Simulation Play(ulong seed = 3)
    {
        var sim = GameSetup.NewGame(TestData.Game, seed).Simulation;
        PoliticsActions.PlayAs(sim.World, Id);
        return sim;
    }

    /// <summary>Штаб тратит все очки: поездки к классам с наибольшей отдачей, на остаток — агитация.</summary>
    static void Spend(World world)
    {
        while (CampaignModel.Estimate(world, world[Id]) is { } outlook)
        {
            var best = Array.IndexOf(outlook.TripGain, outlook.TripGain.Max());
            if (!(outlook.TripGain[best] > 0 && PoliticsActions.CampaignTrip(world, Id, best)) && !PoliticsActions.CampaignMedia(world, Id))
                break;
        }
    }

    static double HeadVotes(World world, string party) =>
        world.Politics![Id].Elections.Last(e => e.Kind == "parliament" && e.Date == Election).Votes.GetValueOrDefault(party);

    [Fact]
    public void CampaignStartsThreeMonthsBeforeTheElection()
    {
        var sim = Play();
        var p = sim.World.Politics![Id];
        sim.RunUntil(new DateOnly(2026, 6, 2));
        Assert.Null(p.Campaign);
        sim.RunUntil(new DateOnly(2026, 7, 2));
        Assert.NotNull(p.Campaign);
        Assert.Equal(Election, p.Campaign!.Election);
        Assert.True(p.Campaign.Points > 0);
        Assert.Contains(sim.World.Journal, e => e.Country == Id && e.Kind == "campaign");
        var snapshot = PoliticsSnapshot.Capture(sim.World, sim.World[Id], sim.World.Politics![Id]).Campaign!;
        Assert.Equal(TestData.Game.Classes.Classes.Count, snapshot.TripGain.Count);
        Assert.All(snapshot.TripGain, g => Assert.True(g >= 0));
        Assert.NotEmpty(snapshot.Options);
        // Другие страны кампанию игрока не ведут, но их партии агитируют сами.
        Assert.All(sim.World.Politics!.Countries.Where(kv => kv.Key != Id), kv => Assert.Null(kv.Value.Campaign));
        sim.RunUntil(new DateOnly(2026, 10, 2));
        Assert.Null(p.Campaign);
    }

    [Fact]
    public void ActiveCampaignWinsVotes()
    {
        var passive = Play();
        var active = Play();
        var party = PoliticsModel.HeadParty(active.World, Id)!.Id;
        for (var date = new DateOnly(2026, 7, 2); date <= new DateOnly(2026, 10, 2); date = date.AddMonths(1))
        {
            passive.RunUntil(date);
            active.RunUntil(date);
            if (date < Election)
                Spend(active.World);
        }
        var gain = HeadVotes(active.World, party) - HeadVotes(passive.World, party);
        Assert.True(gain is > 0.01 and < 0.1, $"кампания прибавила {gain:P1}");
    }

    [Fact]
    public void TripRaisesSupportAmongTheClass()
    {
        var sim = Play();
        sim.RunUntil(new DateOnly(2026, 7, 2));
        var world = sim.World;
        const int workers = 1;
        var before = CampaignModel.Estimate(world, world[Id])!;
        Assert.True(PoliticsActions.CampaignTrip(world, Id, workers));
        var after = CampaignModel.Estimate(world, world[Id])!;
        Assert.True(after.ClassShare[workers] > before.ClassShare[workers]);
        // Очки кончаются.
        while (PoliticsActions.CampaignTrip(world, Id, workers)) { }
        Assert.True(world.Politics![Id].Campaign!.Points < TestData.Game.PoliticsBalance.TripCost);
        Assert.True(world.Politics![Id].Parties.Single(x => x.Id == world.Politics![Id].Campaign!.Party).Campaign[workers] <= TestData.Game.PoliticsBalance.CampaignCap);
    }

    [Fact]
    public void DebateComesAMonthBeforeTheElection()
    {
        var sim = Play();
        var p = sim.World.Politics![Id];
        sim.RunUntil(new DateOnly(2026, 8, 2));
        Assert.Null(p.Campaign!.Debate);
        sim.RunUntil(new DateOnly(2026, 9, 2));
        var debate = p.Campaign!.Debate!;
        Assert.InRange(debate.Axis, 0, PoliticalViews.Axes - 1);
        var snapshot = PoliticsSnapshot.Capture(sim.World, sim.World[Id], sim.World.Politics![Id]).Campaign!.Debate!;
        Assert.Equal(3, snapshot.Answers.Count);
        Assert.All(snapshot.Answers, a => Assert.False(string.IsNullOrWhiteSpace(a)));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Question));
        Assert.Equal(3, snapshot.Gain.Count);
        Assert.True(PoliticsActions.AnswerDebate(sim.World, Id, 1));
        Assert.False(PoliticsActions.AnswerDebate(sim.World, Id, -1), "второй раз не отвечают");
        Assert.Equal(1, debate.Answer);
    }

    [Fact]
    public void PromisesBindTheWinner()
    {
        var sim = Play();
        var world = sim.World;
        var p = world.Politics![Id];
        sim.RunUntil(new DateOnly(2026, 7, 2));
        var option = CampaignModel.Estimate(world, world[Id])!.Options[0];
        Assert.True(PoliticsActions.Pledge(world, Id, option.Law, option.Variant));
        Assert.False(PoliticsActions.Pledge(world, Id, option.Law, option.Variant), "один закон обещают один раз");
        var party = p.Party(p.Campaign!.Party)!;
        // Партия побеждает уверенно.
        for (var date = new DateOnly(2026, 8, 2); date <= new DateOnly(2026, 10, 2); date = date.AddMonths(1))
        {
            party.Momentum = 2;
            sim.RunUntil(date);
        }
        Assert.True(party.Ruling);
        var pledge = Assert.Single(p.Pledges);
        Assert.Equal(Election.AddYears(TestData.Game.PoliticsBalance.PromiseYears), pledge.Deadline);
        sim.RunUntil(pledge.Deadline!.Value.AddMonths(1).AddDays(1));
        if (world.Laws![Id].Current[option.Law] != option.Variant && party.Ruling)
            Assert.Contains(world.Journal, e => e.Country == Id && e.Text.StartsWith("Обещание не выполнено"));
        Assert.Empty(p.Pledges);
    }

    [Fact]
    public void KeptPromiseIsRewarded()
    {
        var sim = Play();
        var world = sim.World;
        var p = world.Politics![Id];
        sim.RunUntil(new DateOnly(2026, 7, 2));
        var option = CampaignModel.Estimate(world, world[Id])!.Options[0];
        PoliticsActions.Pledge(world, Id, option.Law, option.Variant);
        var party = p.Party(p.Campaign!.Party)!;
        for (var date = new DateOnly(2026, 8, 2); date <= new DateOnly(2026, 10, 2); date = date.AddMonths(1))
        {
            party.Momentum = 2;
            sim.RunUntil(date);
        }
        world.Laws![Id].Current[option.Law] = option.Variant;
        sim.RunUntil(world.Date.AddMonths(1));
        Assert.Empty(p.Pledges);
        Assert.Contains(world.Journal, e => e.Country == Id && e.Text.StartsWith("Обещание выполнено"));
    }

    [Fact]
    public void FraudAddsVotesButRisksAScandal()
    {
        var honest = Play();
        var fraud = Play();
        var party = PoliticsModel.HeadParty(fraud.World, Id)!.Id;
        honest.RunUntil(new DateOnly(2026, 7, 2));
        fraud.RunUntil(new DateOnly(2026, 7, 2));
        Assert.True(PoliticsActions.SetFraud(fraud.World, Id, 2));
        honest.RunUntil(new DateOnly(2026, 10, 2));
        fraud.RunUntil(new DateOnly(2026, 10, 2));
        Assert.True(HeadVotes(fraud.World, party) > HeadVotes(honest.World, party) + 0.03);
        Assert.Contains(fraud.World.Journal, e => e.Country == Id && e.Kind == "fraud");

        // Риск выше там, где пресса свободна.
        var world = honest.World;
        Assert.True(CampaignModel.FraudRisk(world, world["SWE"], 2) > CampaignModel.FraudRisk(world, world["RUS"], 2));
        Assert.True(CampaignModel.FraudRisk(world, world["SWE"], 2) > CampaignModel.FraudRisk(world, world["SWE"], 1));
        Assert.Equal(0, CampaignModel.FraudRisk(world, world["SWE"], 0));
    }

    [Fact]
    public void ScandalHurtsLegitimacyAndRadicalisesPeople()
    {
        var calm = Play();
        var hit = Play();
        hit.World.Politics![Id].Scandal = 1;
        calm.RunUntil(new DateOnly(2026, 4, 1));
        hit.RunUntil(new DateOnly(2026, 4, 1));
        Assert.True(hit.World[Id].Stat("legitimacy") < calm.World[Id].Stat("legitimacy") - 5);
        Assert.True(hit.World.Society![Id].Radicalism.Average() > calm.World.Society![Id].Radicalism.Average() + 0.5,
            $"радикализм {hit.World.Society![Id].Radicalism.Average():F2} против {calm.World.Society![Id].Radicalism.Average():F2}");
        var ruling = (World w) => w.Politics![Id].Parties.Where(x => x.Ruling).Sum(x => x.Poll);
        Assert.True(ruling(hit.World) < ruling(calm.World) - 0.03);
        Assert.True(hit.World.Politics![Id].Scandal < 1, "скандал со временем забывается");
    }

    [Fact]
    public void CampaignSurvivesSaveAndLoad()
    {
        var sim = Play();
        sim.RunUntil(new DateOnly(2026, 9, 2));
        PoliticsActions.SetFraud(sim.World, Id, 1);
        PoliticsActions.CampaignMedia(sim.World, Id);
        using var stream = new MemoryStream();
        SaveGame.Write(sim.World, stream);
        stream.Position = 0;
        var loaded = SaveGame.Read(TestData.Game, stream);
        Assert.Equal(SaveGame.Hash(sim.World), SaveGame.Hash(loaded));
        Assert.Equal(1, loaded.Politics![Id].Campaign!.Fraud);
        var resumed = GameSetup.Resume(loaded);
        sim.RunUntil(new DateOnly(2026, 11, 2));
        resumed.RunUntil(new DateOnly(2026, 11, 2));
        Assert.Equal(SaveGame.Hash(sim.World), SaveGame.Hash(resumed.World));
    }
}
