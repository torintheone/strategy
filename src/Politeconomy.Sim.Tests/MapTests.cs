using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Geo;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Views;

namespace Politeconomy.Sim.Tests;

public class MapTests
{
    static readonly WorldMap Map = WorldMap.From(TestData.Game.Map);

    [Theory]
    [InlineData("RUS")]
    [InlineData("USA")]
    [InlineData("BRA")]
    [InlineData("CHN")]
    [InlineData("IND")]
    [InlineData("AUS")]
    [InlineData("DEU")]
    [InlineData("NGA")]
    public void LabelPointIsInsideItsCountry(string id)
    {
        var region = Map.ById(id)!;
        Assert.Equal(id, Map.FindAt(region.LabelX, region.LabelY)?.Id);
    }

    [Fact]
    public void OceanIsEmpty()
    {
        // Долгота 0°, широта 0° — Гвинейский залив.
        Assert.Null(Map.FindAt(0, 0));
    }

    [Fact]
    public void TinyIslandStateIsFoundByMarker()
    {
        var tuvalu = Map.ById("TUV")!;
        Assert.Equal("TUV", Map.FindAt(tuvalu.LabelX + 3, tuvalu.LabelY + 3, markerRadius: 8)?.Id);
    }

    [Fact]
    public void NeutralTerritoriesAreNotCountries()
    {
        var taiwan = Map.ById("TWN")!;
        Assert.False(taiwan.IsCountry);
        Assert.Equal("Тайвань", taiwan.Name);
    }

    [Fact]
    public void BinsAndLegend()
    {
        Assert.Equal(0, MapModeView.ClassOf(1, [2, 4]));
        Assert.Equal(1, MapModeView.ClassOf(2, [2, 4]));
        Assert.Equal(2, MapModeView.ClassOf(9, [2, 4]));

        var data = TestData.Game;
        var growth = new MapModeView(data, data.MapModes.Modes.First(m => m.Id == "growth"));
        Assert.Equal(data.MapModes.Palettes.DivergingNeutral, growth.ValueColor(0));
        Assert.Equal(data.MapModes.Palettes.DivergingNegative[0], growth.ValueColor(-20));
        Assert.Equal(data.MapModes.Palettes.DivergingPositive[^1], growth.ValueColor(20));
        Assert.Equal(data.MapModes.Palettes.NoData, growth.ValueColor(double.NaN));
        Assert.Equal(8, growth.Legend().Count); // 7 классов и «нет данных»
    }

    [Fact]
    public void EveryModeColorsEveryCountry()
    {
        var data = TestData.Game;
        var snapshot = WorldSnapshot.Capture(WorldFactory.Create(data), 1, true, 0);
        foreach (var mode in data.MapModes.Modes)
        {
            var view = new MapModeView(data, mode);
            foreach (var region in Map.Countries)
                Assert.Matches("^#[0-9a-f]{6}$", view.ColorOf(region, snapshot.Countries[region.Id]));
        }
    }
}
