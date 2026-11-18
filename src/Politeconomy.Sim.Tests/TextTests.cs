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
    public void ExplainShowsValueMeaningAndFactorsWithoutSources()
    {
        var def = TestData.Game.Indicator("inflation_pct");
        var stat = new StatValue(8.7, 2026, StatSources.Model, Factors: [new StatFactor("Инфляционные ожидания", 7.9, "percent")]);
        var text = Explain.Stat(def, stat);
        Assert.StartsWith("Инфляция: 8,7%", text);
        Assert.Contains(def.Description, text);
        Assert.Contains("Инфляционные ожидания: 7,9%", text);

        foreach (var hidden in new[] { new StatValue(54.4, null, StatSources.Fill, "медиана: группа дохода", 24), new StatValue(40, 2015, "wb:X") })
        {
            var plain = Explain.Stat(def, hidden);
            Assert.DoesNotContain("Источник", plain);
            Assert.DoesNotContain("Оценка", plain);
            Assert.DoesNotContain("устарели", plain);
            Assert.DoesNotContain("2015", plain);
        }
    }
}
