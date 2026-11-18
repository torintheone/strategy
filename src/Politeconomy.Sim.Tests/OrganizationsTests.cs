using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim.Tests;

public class OrganizationsTests
{
    static Simulation NewGame() => GameSetup.NewGame(TestData.Game, 3).Simulation;

    [Fact]
    public void StartBlocsAndSanctionsAreRealAndAlreadyInTheEconomy()
    {
        var world = NewGame().World;
        Assert.Equal(27, DiplomacyModel.Members(world, "european").Count);
        Assert.Equal(32, DiplomacyModel.Members(world, "atlantic").Count);
        Assert.Equal("full", DiplomacyModel.SanctionLevel(world, "USA", "RUS"));
        Assert.Equal("full", DiplomacyModel.SanctionLevel(world, "DEU", "RUS"));
        Assert.Null(DiplomacyModel.SanctionLevel(world, "CHN", "RUS"));
        Assert.True(DiplomacyModel.UnderCouncilSanctions(world, "PRK"));
        Assert.True(DiplomacyModel.Allies(world, "USA", "JPN"));
        Assert.True(DiplomacyModel.Allies(world, "DEU", "TUR"));
        // Экономика на старте уже живёт с этими санкциями и блоками: поправок нет.
        foreach (var id in new[] { "RUS", "DEU", "IRN", "PRK", "USA" })
        {
            var c = world.Diplomacy![id];
            Assert.Equal(1, c.TradeFactor, 9);
            Assert.Equal(0, c.Effects.GetValueOrDefault("potential_growth"), 9);
            Assert.Equal(0, c.Effects.GetValueOrDefault("spread"), 9);
        }
        Assert.True(world["RUS"].Stat("sanctions_against") > 40);
        Assert.True(world["PRK"].Stat("sanctions_against") > 150, "санкции Совета соблюдают все");
        // Санкции режут торговлю: главный партнёр России — не Германия.
        Assert.True(DiplomacyModel.TradeShare(world, "RUS", "CHN") > DiplomacyModel.TradeShare(world, "RUS", "DEU") * 3);
    }

    [Fact]
    public void SanctionsHurtTheTargetAndTheSanctionerAndCanBeLifted()
    {
        var world = NewGame().World;
        PoliticsActions.PlayAs(world, "USA");
        var relations = DiplomacyModel.Relations(world, "USA", "BRA");
        Assert.True(DiplomacyModel.Impose(world, "USA", "BRA", "full"));
        var (bra, usa) = (world.Diplomacy!["BRA"], world.Diplomacy!["USA"]);
        Assert.True(bra.TradeFactor < 0.95, $"Бразилия: торговля ×{bra.TradeFactor:F3}");
        Assert.True(usa.TradeFactor < 1, "санкции бьют и по своей торговле");
        Assert.True(bra.Effects["potential_growth"] < -0.1 && bra.Effects["spread"] > 0.2);
        Assert.Equal("partial", DiplomacyModel.SanctionLevel(world, "BRA", "USA"));
        Assert.True(DiplomacyModel.Relations(world, "USA", "BRA") < relations - 10);
        Assert.Contains("Санкции", DiplomacyModel.Breakdown(world, "USA", "BRA").Select(f => f.Label));

        Assert.True(DiplomacyModel.Lift(world, "USA", "BRA"));
        Assert.Null(DiplomacyModel.SanctionLevel(world, "BRA", "USA"));
        Assert.Equal(1, world.Diplomacy!["BRA"].TradeFactor, 9);

        // Снять стартовые санкции — цели легче, чем на старте.
        Assert.True(DiplomacyModel.Lift(world, "USA", "RUS"));
        var rus = world.Diplomacy!["RUS"];
        Assert.True(rus.TradeFactor > 1 && rus.Effects["potential_growth"] > 0 && rus.Effects["spread"] < 0);
    }

    [Fact]
    public void BlocsTakeMembersByVoteAndLeavingCosts()
    {
        var world = NewGame().World;
        PoliticsActions.PlayAs(world, "UKR");
        // Украина при военном положении не демократия; Норвегию не пускают те, с кем отношения слабее (единогласие).
        Assert.Contains("демократии", DiplomacyModel.JoinAcceptance(world, "UKR", "european").Reason);
        Assert.False(DiplomacyModel.JoinAcceptance(world, "UKR", "eurasian_union").Accepts);
        Assert.Contains("Америки", DiplomacyModel.JoinAcceptance(world, "UKR", "southern_market").Reason);
        var (accepts, reason) = DiplomacyModel.JoinAcceptance(world, "NOR", "european");
        Assert.False(accepts);
        Assert.Contains("Против:", reason);

        PoliticsActions.PlayAs(world, "HUN");
        var trade = world.Diplomacy!["HUN"].TradeFactor;
        var austria = DiplomacyModel.Relations(world, "HUN", "AUT");
        Assert.True(DiplomacyModel.Leave(world, "HUN", "european"));
        Assert.False(DiplomacyModel.InBloc(world, "european", "HUN"));
        Assert.True(world.Diplomacy!["HUN"].TradeFactor < trade - 0.05, "без единого рынка торговля падает");
        Assert.True(DiplomacyModel.Relations(world, "HUN", "AUT") < austria - 10);
        // Вернуться сразу не выйдет: после выхода отношения с членами хуже порога.
        Assert.False(DiplomacyModel.Join(world, "HUN", "european"));
    }

    [Fact]
    public void CouncilVotesByRelationsAndPermanentMembersCanVeto()
    {
        var sim = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "USA");
        var forecast = DiplomacyModel.Forecast(world, "sanctions", "USA", "IRN");
        Assert.Contains("RUS", forecast.Vetoes);
        Assert.False(forecast.Passed);
        Assert.False(DiplomacyModel.ProposeResolution(world, "USA", "sanctions", "IRN"));
        Assert.Contains(world.Journal, e => e.Kind == "council" && e.Text.Contains("вето"));
        Assert.False(DiplomacyModel.CanPropose(world, "USA", "condemn", "RUS").Allowed, "не чаще раза в три месяца");

        PoliticsActions.PlayAs(world, "DEU");
        var debt = world.Economy!["ETH"].DebtRatio;
        Assert.True(DiplomacyModel.ProposeResolution(world, "DEU", "aid", "ETH"));
        Assert.True(world.Economy!["ETH"].DebtRatio < debt - 0.01);
        Assert.Equal(15, DiplomacyModel.Security(world).Count);

        // 1 января 2027 года пятеро выборных уступают места странам своих регионов.
        sim.RunUntil(new DateOnly(2027, 1, 2));
        var seats = world.Diplomacy!.Seats;
        Assert.Equal(10, seats.Count);
        Assert.DoesNotContain(seats, s => s.Country == "DNK");
        Assert.Equal(5, seats.Count(s => s.Until == 2028));
        Assert.Equal(2, seats.Count(s => s.Until == 2028 && world[s.Country].Info.Region == "Европа"));
    }

    [Fact]
    public void CurrencyCouncilLendsCheaplyWithConditions()
    {
        var world = NewGame().World;
        PoliticsActions.PlayAs(world, "USA");
        Assert.False(DiplomacyModel.FundOffer(world, "USA").Accepts, "богатым странам Совет не нужен");
        var junk = world.Countries.First(c => world.Economy![c.Id].Rating < 9 && c.Info.UnMember && world.Economy[c.Id].DebtRatio > 0.3
                                              && !DiplomacyModel.UnderCouncilSanctions(world, c.Id) && DiplomacyModel.FundOffer(world, c.Id).Accepts).Id;
        PoliticsActions.PlayAs(world, junk);
        var e = world.Economy![junk];
        var rate = e.EffectiveRate;
        Assert.True(DiplomacyModel.RequestLoan(world, junk));
        Assert.True(e.EffectiveRate < rate, "часть долга перекредитована дешевле");
        Assert.True(world.Diplomacy![junk].Effects["spread"] < 0);
        Assert.False(DiplomacyModel.FundOffer(world, junk).Accepts, "вторая программа сразу не нужна");
        // Национализация нарушает условия: программа прекращена, занимать дороже.
        DiplomacyModel.LawPassed(world, junk, "state_ownership", "nationalization");
        Assert.Equal("failed", world.Diplomacy!.Programs[junk].Status);
        Assert.True(world.Diplomacy![junk].Effects["spread"] > 0);
    }

    [Fact]
    public void PowerIndexRanksTheGreatPowers()
    {
        var world = NewGame().World;
        var power = world.Diplomacy!.Power;
        Assert.Equal(1, power["USA"].Rank);
        Assert.Equal(2, power["CHN"].Rank);
        Assert.True(power["RUS"].Rank <= 6 && power["IND"].Rank <= 10 && power["DEU"].Rank <= 6);
        Assert.True(power["TUV"].Index < 3 && power["TUV"].Rank > 150, $"Тувалу {power["TUV"].Index:F1}, {power["TUV"].Rank}-е место");
        var stat = world["CHN"].Stats["power_index"];
        Assert.Equal(stat.Value, stat.Factors!.Sum(f => f.Value), 6);
    }

    [Fact]
    public void SanctionsBlocsAndProgramsSurviveSaving()
    {
        var sim = NewGame();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "HUN");
        DiplomacyModel.Impose(world, "HUN", "SRB", "partial");
        DiplomacyModel.Leave(world, "HUN", "atlantic");
        sim.RunUntil(world.Date.AddMonths(2));
        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = GameSetup.Resume(SaveGame.Read(TestData.Game, stream));
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded.World));
        Assert.Equal(world.Diplomacy!.Relations, loaded.World.Diplomacy!.Relations);
        Assert.Equal(world.Diplomacy!["HUN"].TradeFactor, loaded.World.Diplomacy!["HUN"].TradeFactor, 12);
        sim.RunUntil(world.Date.AddMonths(3));
        loaded.RunUntil(world.Date);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded.World));
    }
}
