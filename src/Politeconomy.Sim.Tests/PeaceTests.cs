using Politeconomy.Sim.Ai;
using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Geo;
using Politeconomy.Sim.Military;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;
using Politeconomy.Sim.Views;

namespace Politeconomy.Sim.Tests;

public class PeaceTests
{
    static readonly WorldMap Map = WorldMap.From(TestData.Game.Map);

    /// <summary>Самый населённый регион Гайаны, кроме столицы.</summary>
    static string GuyanaRegion(Model.World world) =>
        world.Regions.Where(r => r.Owner == "GUY" && r.Id != world.War!.Map.Capitals["GUY"]).MaxBy(r => r.Info.Population)!.Id;

    [Fact]
    public void AnnexedRegionTakesPeopleAndEconomyAndIsSaved()
    {
        var sim = TestData.WithoutAi(3);
        var world = sim.World;
        var region = GuyanaRegion(world);
        var share = world.RegionById(region)!.Info.Population / world.Regions.Where(r => r.Owner == "GUY").Sum(r => r.Info.Population);
        double Pop(string id) => world.Demography!.Countries[id].Total;
        var (guy, ven) = (Pop("GUY"), Pop("VEN"));
        var gdp = world.Economy!["GUY"].RealGdp;
        world.TransferControl(region, "VEN");
        PeaceModel.Annex(world, region, "VEN");

        var r = world.RegionById(region)!;
        Assert.Equal(("VEN", "VEN"), (r.Owner, r.Controller));
        Assert.Equal(guy * (1 - share), Pop("GUY"), 3);
        Assert.Equal(guy + ven, Pop("GUY") + Pop("VEN"), 3);
        sim.RunUntil(world.Date.AddMonths(1));
        Assert.InRange(world.Economy!["GUY"].RealGdp / gdp, (1 - share) * 0.9, (1 - share) * 1.15);
        // Занятость больше не режет выпуск: регион теперь свой.
        Assert.Equal(1, world.War!.Armies["GUY"].Output, 2);

        // Карта: регион в цвете нового владельца, щелчок по нему — Венесуэла.
        var snapshot = WorldSnapshot.Capture(world, 1, true, 0);
        Assert.Equal("VEN", snapshot.OwnerOf(region, "GUY"));
        var political = new MapModeView(TestData.Game, Map, TestData.Game.MapModes.Modes.First(m => m.Kind == "political"));
        Assert.Equal(political.PoliticalColor("VEN"), political.ColorOf(Map.Region(region)!, snapshot));

        // Сохранение: владелец остаётся, партия продолжается так же.
        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = SaveGame.Read(TestData.Game, stream);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
        Assert.Equal("VEN", loaded.RegionById(region)!.Owner);
        var resumed = new Simulation(loaded, TestData.QuietSystems());
        resumed.RunUntil(world.Date.AddMonths(1));
        sim.RunUntil(world.Date.AddMonths(1));
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
    }

    [Fact]
    public void CapitulationEndsInTreatyThatTransfersClaimedRegions()
    {
        var sim = TestData.WithoutAi(3);
        var world = sim.World;
        var war = WarModel.Declare(world, "VEN", "GUY")!;
        for (var month = 0; month < 24 && world.War!.Treaties.Count == 0; month++)
            sim.RunUntil(world.Date.AddMonths(1));
        var treaty = Assert.Single(world.War!.Treaties);
        Assert.Equal(("VEN", "GUY"), (treaty.Winner, treaty.Loser));
        Assert.NotEmpty(treaty.Terms.Annex);
        Assert.Contains(treaty.Terms.Annex, r => war.Goal.Contains(r));
        Assert.All(treaty.Terms.Annex, r => Assert.Equal("VEN", world.RegionById(r)!.Owner));
        // Остальное занятое вернулось, перемирие сменилось миром, столица осталась у Гайаны.
        Assert.DoesNotContain(world.Regions, r => r.IsOccupied && (r.Owner == "GUY" || r.Owner == "VEN"));
        Assert.Null(WarModel.CeasefireBetween(world, "VEN", "GUY"));
        Assert.Equal("GUY", world.RegionById(world.War.Map.Capitals["GUY"])!.Owner);
        Assert.NotNull(PeaceModel.TreatyBetween(world, "VEN", "GUY"));
        Assert.Contains(world.Ai!["VEN"].Decisions, d => d.Kind == "peace" && d.Why.Contains("счёт войны"));
    }

    [Fact]
    public void FrozenConflictStaysFrozenAtStart()
    {
        var world = GameSetup.NewGame(TestData.Game, 1).Simulation.World;
        // Россия держит Крым: счёт в её пользу, но Украина не устала — земли не уступает; белый мир Россия не примет.
        var score = PeaceModel.Score(world, "RUS", "UKR");
        Assert.True(score > 3, $"счёт {score}");
        var crimea = PeaceModel.Annexable(world, "RUS", "UKR").Select(r => r.Id).ToList();
        Assert.NotEmpty(crimea);
        Assert.False(PeaceModel.Accepts(world, "UKR", "RUS", "UKR", new PeaceTerms(crimea, 0, false, false)).Yes);
        Assert.False(PeaceModel.Accepts(world, "RUS", "RUS", "UKR", PeaceTerms.White).Yes);
        var sim = GameSetup.NewGame(TestData.Game, 1).Simulation;
        sim.RunUntil(sim.World.Date.AddMonths(13));
        Assert.DoesNotContain(sim.World.War!.Treaties, t => t.Between("RUS", "UKR"));
    }

    [Fact]
    public void PlayerPeaceWithReparationsAndNeutrality()
    {
        var sim = TestData.WithoutAi(3);
        var world = sim.World;
        PoliticsActions.PlayAs(world, "RUS");
        WarModel.Declare(world, "RUS", "UKR");
        // Россия держит Крым и Севастополь с 2014 года: их и требует.
        var terms = new PeaceTerms(PeaceModel.Annexable(world, "RUS", "UKR").Select(r => r.Id).ToList(), 0.5, false, true);
        // Не устала — отказывается; устала — соглашается.
        Assert.False(PeaceModel.ProposeByPlayer(world, "RUS", "UKR", terms));
        Assert.Contains(world.Journal, j => j.Kind == "peace" && j.Text.Contains("отвергает"));
        world.War!.Armies["UKR"].Exhaustion = 90;
        Assert.True(PeaceModel.Cost(world, "RUS", terms) <= PeaceModel.Score(world, "RUS", "UKR") + TestData.Game.PeaceBalance.Slack);
        Assert.True(PeaceModel.ProposeByPlayer(world, "RUS", "UKR", terms));
        Assert.Empty(world.War.Wars);
        Assert.All(terms.Annex, r => Assert.Equal("RUS", world.RegionById(r)!.Owner));
        Assert.Contains(world.Journal, j => j.Kind == "peace" && j.Text.StartsWith("Подписан мирный договор"));
        // Нейтралитет: военные блоки и союзы закрыты; репарации идут каждый месяц.
        Assert.Contains("нейтральна", DiplomacyModel.Acceptance(world, "UKR", "POL", "alliance").Reason);
        var debt = world.Economy!["UKR"].Debt;
        sim.RunUntil(world.Date.AddMonths(1));
        var treaty = Assert.Single(world.War.Treaties);
        Assert.True(treaty.Paid > 0);
        Assert.True(world.Economy["UKR"].Debt > debt);
        // Нарушить мир можно, но это нападение без оправданий.
        var war = WarModel.Declare(world, "RUS", "UKR")!;
        Assert.Contains("нарушив мирный договор", war.News[0].Text);
        Assert.Null(PeaceModel.TreatyBetween(world, "RUS", "UKR"));
    }

    [Fact]
    public void AiOffersPeaceToTiredPlayerAndPlayerAccepts()
    {
        var sim = TestData.WithoutAi(3);
        var world = sim.World;
        PoliticsActions.PlayAs(world, "GUY");
        WarModel.Declare(world, "VEN", "GUY");
        for (var month = 0; month < 24 && world.War!.Offers.Count == 0 && world.War.Treaties.Count == 0; month++)
            sim.RunUntil(world.Date.AddMonths(1));
        var offer = Assert.Single(world.War!.Offers);
        Assert.Equal(("VEN", "GUY"), (offer.From, offer.To));
        Assert.Contains(world.Journal, j => j.Kind == "peace" && j.Text.Contains("предлагает мир"));
        var option = Assert.Single(WorldSnapshot.Capture(world, 1, true, 0).Wars.Peace);
        Assert.False(option.Winner);
        Assert.NotNull(option.Offer);
        Assert.True(PeaceModel.AcceptOffer(world, "GUY", "VEN"));
        Assert.Single(world.War.Treaties);
        Assert.Empty(world.War.Offers);
    }

    [Fact]
    public void AiDoesNotAttackPeacePartner()
    {
        var world = GameSetup.NewGame(TestData.Game, 9).Simulation.World;
        PeaceModel.Sign(world, "GUY", "VEN", PeaceTerms.White);
        var ai = world.Ai!["VEN"];
        ai.Priorities = [0.1, 0.1, 0.7, 0.1];
        world.War!.Strength["VEN"] *= 1000;
        for (var i = 0; i < 100; i++)
        {
            ai.Last.Remove("war");
            AiWar.Consider(new AiTurn(world, world["VEN"], ai));
        }
        Assert.False(WarModel.AtWar(world, "VEN"));
    }
}
