using Politeconomy.Sim.Economy;
using Politeconomy.Sim.Economy.Systems;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim.Tests;

public class EconomyTests
{
    static World Calibrated(ulong seed = 1)
    {
        var world = WorldFactory.Create(TestData.Game, seed);
        EconomyModel.Setup(world);
        return world;
    }

    static double Data(World world, string country, string stat) => world[country].Info.Stats[stat].Value;

    [Theory]
    [InlineData("USA")]
    [InlineData("DEU")]
    [InlineData("RUS")]
    [InlineData("BRA")]
    [InlineData("TWN")]
    public void CalibrationReproducesData(string id)
    {
        var world = Calibrated();
        var country = world[id];
        foreach (var stat in new[] { "gdp_usd", "population", "gov_debt_pct_gdp", "inflation_pct", "unemployment_pct", "gdp_growth_pct" })
            Assert.Equal(Data(world, id, stat), country.Stat(stat), Data(world, id, stat) * 1e-6 + 1e-6);
        Assert.Equal(Data(world, id, "budget_balance_pct_gdp"), country.Stat("budget_balance_pct_gdp"), 0.2);
        Assert.Equal(StatSources.Model, country.Stats["gdp_usd"].Source);
    }

    [Fact]
    public void IndustriesAddUpToGdpAndSectors()
    {
        var world = Calibrated();
        var industries = world.Data.Industries.Industries;
        foreach (var id in new[] { "USA", "SAU", "IND", "NGA" })
        {
            var e = world.Economy![id];
            Assert.Equal(e.RealGdp, e.Output.Sum(), e.RealGdp * 1e-9);
            Assert.Equal(1, e.EmploymentShare.Sum(), 1e-9);
            var dataAgri = Data(world, id, "agriculture_pct_gdp");
            var dataTotal = dataAgri + Data(world, id, "industry_pct_gdp") + Data(world, id, "services_pct_gdp");
            var agri = industries.Select((ind, i) => ind.Sector == "agriculture" ? e.Output[i] : 0).Sum() / e.RealGdp;
            Assert.Equal(dataAgri / dataTotal, agri, 0.005);
        }
        // У Саудовской Аравии нефть и газ — крупнейшая отрасль, в США — нет.
        var oil = industries.Select((ind, i) => (ind, i)).Single(t => t.ind.Id == "oil_gas").i;
        Assert.Equal(world.Economy!["SAU"].Output.Max(), world.Economy!["SAU"].Output[oil]);
        Assert.True(world.Economy!["USA"].Output[oil] < 0.05 * world.Economy!["USA"].RealGdp);
    }

    [Fact]
    public void CurrencyUnionsShareOneRate()
    {
        var world = Calibrated();
        var economy = world.Economy!;
        Assert.Equal(economy["DEU"].PolicyRate, economy["FRA"].PolicyRate);
        Assert.Equal(economy["DEU"].PolicyRate, economy["XKX"].PolicyRate);
        Assert.Equal("float", economy.Currencies["EUR"].Regime);
        Assert.Equal("peg", economy.Currencies["XOF"].Regime);
        Assert.Equal(economy.Currencies["EUR"].PolicyRate + economy.Currencies["XOF"].PegSpread, economy.Currencies["XOF"].PolicyRate, 1e-12);
        Assert.Equal("USA", economy.Currencies["USD"].Home);
        Assert.Contains("ECU", economy.Currencies["USD"].Members);
    }

    [Fact]
    public void WarmupKeepsIndicatorsNearData()
    {
        var start = GameSetup.NewGame(TestData.Game);
        Assert.NotNull(start.Warmup);
        Assert.True(start.Warmup!.GdpShareWithinTolerance >= 0.9, $"в норме {start.Warmup.GdpShareWithinTolerance:P0} мирового ВВП");
        Assert.True(start.Warmup.CountryShareWithinTolerance >= 0.8, $"в норме {start.Warmup.CountryShareWithinTolerance:P0} стран");
        // После разогрева уровни вернулись к данным.
        var world = start.Simulation.World;
        Assert.Equal(Data(world, "USA", "gdp_usd"), world["USA"].Stat("gdp_usd"), 1e3);
        Assert.Equal(Data(world, "JPN", "gov_debt_pct_gdp"), world["JPN"].Stat("gov_debt_pct_gdp"), 1e-6);
    }

    [Fact]
    public void TwentyYearsStayFiniteAndGrow()
    {
        var sim = GameSetup.NewGame(TestData.Game).Simulation;
        var world = sim.World;
        var start = world.Economy!.Countries.Values.Sum(e => e.RealGdp);
        sim.RunUntil(world.Date.AddYears(20));
        var end = world.Economy!.Countries.Values.Sum(e => e.RealGdp);
        var growth = Math.Pow(end / start, 1.0 / 20) - 1;
        Assert.InRange(growth, 0.01, 0.05);
        foreach (var country in world.Countries)
            foreach (var (key, stat) in country.Stats)
                Assert.False(double.IsNaN(stat.Value) || double.IsInfinity(stat.Value), $"{country.Id}.{key}");
    }

    [Fact]
    public void SameSeedSameEconomy()
    {
        string Run(ulong seed)
        {
            var sim = GameSetup.NewGame(TestData.Game, seed).Simulation;
            sim.RunUntil(sim.World.Date.AddYears(2));
            return SaveGame.Hash(sim.World);
        }
        Assert.Equal(Run(5), Run(5));
        Assert.NotEqual(Run(5), Run(6));
    }

    [Fact]
    public void SaveAndLoadContinueIdentically()
    {
        var sim = GameSetup.NewGame(TestData.Game, 11).Simulation;
        sim.RunUntil(sim.World.Date.AddMonths(14));
        using var stream = new MemoryStream();
        SaveGame.Write(sim.World, stream);
        stream.Position = 0;
        var loaded = GameSetup.Resume(SaveGame.Read(TestData.Game, stream));
        Assert.Equal(SaveGame.Hash(sim.World), SaveGame.Hash(loaded.World));
        Assert.NotNull(loaded.World["USA"].Stats["inflation_pct"].Factors);

        sim.RunUntil(sim.World.Date.AddMonths(13));
        loaded.RunUntil(loaded.World.Date.AddMonths(13));
        Assert.Equal(SaveGame.Hash(sim.World), SaveGame.Hash(loaded.World));
    }

    /// <summary>Два одинаковых мира: в первом что-то меняем, оба живут год с одинаковыми случайными толчками.</summary>
    static (World Changed, World Base) Compare(Action<World> change, int months = 12)
    {
        var a = GameSetup.NewGame(TestData.Game, 3).Simulation;
        var b = GameSetup.NewGame(TestData.Game, 3).Simulation;
        change(a.World);
        a.RunUntil(a.World.Date.AddMonths(months));
        b.RunUntil(b.World.Date.AddMonths(months));
        return (a.World, b.World);
    }

    [Fact]
    public void BudgetExpansionRaisesOutputDeficitAndInflation()
    {
        var (more, baseline) = Compare(w =>
        {
            var e = w.Economy!["BRA"];
            for (var k = 0; k < e.SpendingPlan.Length; k++)
                e.SpendingPlan[k] *= 1.1;
        }, months: 6);
        var x = more.Economy!["BRA"];
        var y = baseline.Economy!["BRA"];
        Assert.True(x.Gap > y.Gap, "расходы разгоняют экономику");
        Assert.True(x.BalanceRatio < y.BalanceRatio - 0.02, "и увеличивают дефицит");
        Assert.True(x.Inflation > y.Inflation, "и инфляцию");
        Assert.True(x.Unemployment < y.Unemployment, "и снижают безработицу");
    }

    [Fact]
    public void OilPriceHelpsExportersAndHurtsImporters()
    {
        var (expensive, baseline) = Compare(w => w.Economy!.OilPrice = 1.6, months: 3);
        var sau = expensive.Economy!["SAU"];
        Assert.True(sau.NominalGdp > baseline.Economy!["SAU"].NominalGdp * 1.05, "у экспортёра растёт ВВП в деньгах");
        Assert.True(sau.TotalRevenue > baseline.Economy!["SAU"].TotalRevenue * 1.05, "и доходы бюджета");
        Assert.True(sau.CurrentAccount > baseline.Economy!["SAU"].CurrentAccount, "и текущий счёт");
        Assert.True(expensive.Economy!["JPN"].Inflation > baseline.Economy!["JPN"].Inflation, "у импортёра растут цены");
    }

    [Fact]
    public void HigherInflationMakesCentralBankRaiseRate()
    {
        var (hot, baseline) = Compare(w =>
        {
            var e = w.Economy!["BRA"];
            e.Inflation += 0.05;
            e.Expectations += 0.03;
        }, months: 2);
        Assert.True(hot.Economy!["BRA"].PolicyRate > baseline.Economy!["BRA"].PolicyRate + 0.003);
    }

    [Fact]
    public void UnpayableDebtEndsInDefault()
    {
        var sim = GameSetup.NewGame(TestData.Game, 4).Simulation;
        var e = sim.World.Economy!["ARG"];
        e.Debt = 4 * e.NominalGdp;
        sim.RunUntil(new DateOnly(sim.World.Date.Year + 1, 1, 2));
        Assert.Equal(1, e.Defaults);
        Assert.True(e.DebtRatio < 2.5);
        Assert.True(e.Rating <= 2, $"рейтинг после дефолта {RatingSystem.Letter(e.Rating)}");
        Assert.Equal(1, sim.World["ARG"].Stat("defaults"));
    }

    [Theory]
    [InlineData(21, "AAA")]
    [InlineData(20.4, "AA+")]
    [InlineData(13, "BBB")]
    [InlineData(7, "B")]
    [InlineData(0, "D")]
    public void RatingLetters(double score, string letter) => Assert.Equal(letter, RatingSystem.Letter(score));

    [Fact]
    public void StartingRatingsLookPlausible()
    {
        var world = Calibrated();
        double R(string id) => world.Economy![id].Rating;
        Assert.True(R("USA") >= 19 && R("DEU") >= 18, "США и Германия — уровень AA и выше");
        Assert.True(R("DEU") > R("ITA") && R("ITA") > R("EGY"), "Германия надёжнее Италии, Италия — Египта");
        Assert.True(R("ETH") < 10, "бедная страна — ниже инвестиционного уровня");
    }
}
