using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Tests;

public class DiplomacyTests
{
    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 3).Simulation;

    [Fact]
    public void TradeFollowsSizeAndGeography()
    {
        var world = NewGame().World;
        var d = world.Diplomacy!;
        Assert.All(d.Trade, row => Assert.Equal(1, row.Sum(), 6));
        string Top(string id) => world.Countries.Where(c => c.Id != id).MaxBy(c => DiplomacyModel.TradeShare(world, id, c.Id))!.Id;
        Assert.Equal("USA", Top("CAN"));
        Assert.Equal("USA", Top("MEX"));
        Assert.True(DiplomacyModel.TradeShare(world, "DEU", "FRA") > DiplomacyModel.TradeShare(world, "DEU", "BRA") * 5);
    }

    [Fact]
    public void StartRelationsLookLikeTheRealWorld()
    {
        var world = NewGame().World;
        double R(string a, string b) => DiplomacyModel.Relations(world, a, b);
        Assert.True(R("USA", "CAN") > 40);
        Assert.True(R("USA", "RUS") < -25);
        Assert.True(R("IND", "PAK") < -30);
        Assert.True(R("ISR", "IRN") < -60);
        Assert.True(R("RUS", "BLR") > 40);
        Assert.Equal(R("USA", "RUS"), R("RUS", "USA"), 9);
        Assert.Equal(R("DEU", "FRA"), DiplomacyModel.Breakdown(world, "DEU", "FRA").Sum(f => f.Value), 6);
        Assert.False(double.IsFinite(world["CAN"].Stat("relations_player")), "без игрока отношения с ним не публикуются");
    }

    [Fact]
    public void RelationsFollowAChangeOfPowerTheSameDay()
    {
        var sim = NewGame();
        var world = sim.World;
        sim.RunUntil(world.Date.AddDays(3));
        double Sum(string a, string b) => DiplomacyModel.Breakdown(world, a, b).Sum(f => f.Value);
        var before = DiplomacyModel.Relations(world, "BRA", "USA");
        PowerModel.MilitaryCoup(world, world["BRA"], world.Politics!["BRA"]);
        Assert.NotEqual(before, Sum("BRA", "USA"), 3);
        sim.Settle();
        Assert.Equal(Sum("BRA", "USA"), DiplomacyModel.Relations(world, "BRA", "USA"), 6);

        // Без игрока (переворот посреди месяца) — со следующего дня, не дожидаясь 1-го числа.
        PowerModel.MilitaryCoup(world, world["ARG"], world.Politics!["ARG"]);
        sim.StepDay();
        Assert.True(world.Date.Day != 1);
        Assert.Equal(Sum("ARG", "USA"), DiplomacyModel.Relations(world, "ARG", "USA"), 6);

        // Пересчёт только пар со сменившими власть странами даёт то же, что полный пересчёт после загрузки.
        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = GameSetup.Resume(SaveGame.Read(TestData.Game, stream)).World;
        Assert.Equal(world.Diplomacy!.Relations, loaded.Diplomacy!.Relations);
    }

    [Fact]
    public void VisitsHelpButFadeAndCanBeCriticised()
    {
        var sim = NewGame();
        var world = sim.World;
        Assert.False(DiplomacyModel.Visit(world, "DEU", "BRA"), "только глава своей страны");
        PoliticsActions.PlayAs(world, "DEU");
        var before = DiplomacyModel.Relations(world, "DEU", "BRA");
        Assert.True(DiplomacyModel.Visit(world, "DEU", "BRA"));
        Assert.Equal(before + TestData.Game.DiplomacyBalance.VisitRelations, DiplomacyModel.Relations(world, "DEU", "BRA"), 6);
        Assert.False(DiplomacyModel.Visit(world, "DEU", "BRA"), "не чаще раза в полгода");
        Assert.True(world["BRA"].Stat("relations_player") > before);
        var popularity = world.Government!["DEU"].Leader.Popularity;
        Assert.True(DiplomacyModel.Visit(world, "DEU", "RUS"));
        Assert.True(world.Government!["DEU"].Leader.Popularity < popularity, "визит к недругу критикуют");
        sim.RunUntil(world.Date.AddYears(4));
        Assert.True(DiplomacyModel.Relations(world, "DEU", "BRA") < before + 3, "память о визите тает");
    }

    [Fact]
    public void FreeTradeNeedsFriendsAndRaisesTrade()
    {
        var sim = NewGame();
        var same = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "CAN");
        Assert.False(DiplomacyModel.Propose(world, "CAN", "RUS", "free_trade"));
        Assert.Contains(world.Journal, e => e.Country == "CAN" && e.Kind == "diplomacy" && e.Text.Contains("отказывается"));
        Assert.True(DiplomacyModel.Propose(world, "CAN", "USA", "free_trade"));
        var c = world.Diplomacy!["CAN"];
        Assert.True(c.TradeFactor > 1.15, $"торговля ×{c.TradeFactor:F2}");
        Assert.True(c.Productivity > 1);
        Assert.True(c.Mood[ClassIds.Skilled] < -1, "рабочим труднее конкурировать с импортом");
        sim.RunUntil(world.Date.AddYears(1));
        same.RunUntil(same.World.Date.AddYears(1));
        Assert.True(world.Economy!["CAN"].Exports > same.World.Economy!["CAN"].Exports * 1.1);

        // Договоры переживают сохранение: торговля пар и отношения пересчитываются после загрузки.
        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = GameSetup.Resume(SaveGame.Read(TestData.Game, stream)).World;
        Assert.Equal(DiplomacyModel.Relations(world, "CAN", "USA"), DiplomacyModel.Relations(loaded, "CAN", "USA"), 9);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
    }

    [Fact]
    public void InvestorsLeaveWhenIndustryIsNationalised()
    {
        var world = NewGame().World;
        PoliticsActions.PlayAs(world, "MEX");
        Assert.True(DiplomacyModel.Propose(world, "MEX", "USA", "investment"));
        Assert.True(world.Diplomacy!["MEX"].Effects["potential_growth"] > 0.05);
        var before = DiplomacyModel.Relations(world, "MEX", "USA");
        DiplomacyModel.LawPassed(world, "MEX", "state_ownership", "nationalization");
        Assert.False(DiplomacyModel.HasTreaty(world, "MEX", "USA", "investment"));
        Assert.True(DiplomacyModel.Relations(world, "MEX", "USA") < before - 10);
    }

    [Fact]
    public void AidWinsFriendsAndExpulsionLosesThem()
    {
        var world = NewGame().World;
        PoliticsActions.PlayAs(world, "USA");
        var (usa, eth) = (world.Economy!["USA"].Debt, world.Economy!["ETH"].Debt);
        var before = DiplomacyModel.Relations(world, "USA", "ETH");
        Assert.True(DiplomacyModel.Aid(world, "USA", "ETH"));
        Assert.True(DiplomacyModel.Relations(world, "USA", "ETH") > before + 20, "для Эфиопии это большие деньги");
        Assert.True(world.Economy!["USA"].Debt > usa && world.Economy!["ETH"].Debt < eth);
        var china = DiplomacyModel.Relations(world, "USA", "CHN");
        Assert.True(DiplomacyModel.Expel(world, "USA", "CHN"));
        Assert.Equal(china - TestData.Game.DiplomacyBalance.ExpelRelations, DiplomacyModel.Relations(world, "USA", "CHN"), 6);
    }

    [Fact]
    public void EnergyDealMakesFuelCheaperAndScienceCooperationMakesTechCheaper()
    {
        var sim = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "DEU");
        Assert.False(DiplomacyModel.Acceptance(world, "DEU", "FRA", "energy").Accepts, "обе страны покупают топливо");
        Assert.True(DiplomacyModel.Propose(world, "DEU", "NOR", "energy"));
        Assert.Equal(-TestData.Game.DiplomacyBalance.EnergyFuel, world.Diplomacy!["DEU"].Effects["fuel_cost"], 9);
        Assert.True(world.Diplomacy!["NOR"].Effects["potential_growth"] > 0);

        var india = world["IND"];
        world.PlayerCountry = "IND";
        var tech = TestData.Game.Science.Technologies.First(t => world.Science!["JPN"].Researched.Contains(t.Id)
                                                                 && Science.ScienceModel.Available(TestData.Game, world.Science!["IND"], t));
        var cost = Science.ScienceModel.Cost(world, india, tech);
        Assert.True(DiplomacyModel.Propose(world, "IND", "JPN", "science"));
        Assert.Equal(cost * (1 - TestData.Game.DiplomacyBalance.ScienceDiscount), Science.ScienceModel.Cost(world, india, tech), 6);
    }
}
