using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Tests;

public class TextTests
{
    const char Nb = Fmt.Nbsp;

    [Fact]
    public void FormatsNumbersTheRussianWay()
    {
        Assert.Equal($"143,5{Nb}млн", Fmt.Value("people", 143_513_328));
        Assert.Equal($"1,41{Nb}млрд", Fmt.Value("people", 1_406_585_000));
        Assert.Equal($"2,56{Nb}трлн{Nb}$", Fmt.Value("money_big", 2.561e12));
        Assert.Equal($"17{Nb}850{Nb}$", Fmt.Value("money", 17_850.4));
        Assert.Equal("8,7%", Fmt.Value("percent", 8.71));
        Assert.Equal("−0,2%", Fmt.Value("percent_signed", -0.2));
        Assert.Equal("+2,2%", Fmt.Value("percent_signed", 2.2));
        Assert.Equal($"17,9%{Nb}ВВП", Fmt.Value("percent_gdp", 17.9));
        Assert.Equal($"−7,9%{Nb}ВВП", Fmt.Value("percent_gdp_signed", -7.9));
        Assert.Equal("—", Fmt.Value("percent", double.NaN));
        Assert.Equal($"5{Nb}млн", Fmt.Compact("people", 5_000_000));
        Assert.Equal($"1{Nb}января 2026", Fmt.Date(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void ExplainsSourcesAndEstimates()
    {
        var def = TestData.Game.Indicator("gov_debt_pct_gdp");
        var real = Explain.Stat(def, new StatValue(17.9, 2024, "wb:GC.DOD.TOTL.GD.ZS"));
        Assert.Contains("Всемирный банк", real);
        Assert.Contains("2024", real);

        var estimate = Explain.Stat(def, new StatValue(54.4, null, StatSources.Fill, "медиана: группа дохода", 24));
        Assert.Contains("Оценка", estimate);
        Assert.Contains("24", estimate);

        var old = Explain.Stat(def, new StatValue(40, 2015, "wb:X"));
        Assert.Contains("устарели", old);

        var imf = Explain.Stat(def, new StatValue(62.7, 2025, "imf:GGXWDG_NGDP"));
        Assert.Contains("МВФ", imf);
        Assert.Contains("Natural Earth", Explain.Source(new StatValue(326000, 2017, "natural_earth")));
    }
}
