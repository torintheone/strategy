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
    [InlineData("TWN")]
    public void CountryLabelPointIsInsideOneOfItsRegions(string id)
    {
        var country = Map.Country(id)!;
        Assert.Equal(id, Map.FindRegionAt(country.LabelX, country.LabelY)?.Country);
    }

    [Fact]
    public void EveryRegionLabelFindsItsRegion()
    {
        var misses = Map.Regions
            .Where(r => r.Main.Area > 50 && Map.FindRegionAt(r.LabelX, r.LabelY)?.Id != r.Id)
            .Select(r => r.Id)
            .ToList();
        Assert.True(misses.Count < Map.Regions.Count / 100, "подпись вне своего региона: " + string.Join(", ", misses.Take(20)));
    }

    [Fact]
    public void OceanIsEmpty()
    {
        // Долгота 0°, широта 0° — Гвинейский залив.
        Assert.Null(Map.FindRegionAt(0, 0));
    }

    [Fact]
    public void TinyIslandStateIsFoundByMarker()
    {
        var tuvalu = Map.Country("TUV")!;
        Assert.Equal("TUV", Map.FindMarkerAt(tuvalu.LabelX + 3, tuvalu.LabelY + 3, 8, _ => true)?.Id);
        Assert.Null(Map.FindMarkerAt(tuvalu.LabelX + 3, tuvalu.LabelY + 3, 8, _ => false));
    }

    [Fact]
    public void WesternSaharaIsNeutral()
    {
        var sahara = Map.Regions.Single(r => r.Territory == "SAH");
        Assert.True(sahara.IsNeutral);
        Assert.Equal("Западная Сахара", Map.Territory("SAH")!.Name);
    }

    [Fact]
    public void BinsAndLegend()
    {
        Assert.Equal(0, MapModeView.ClassOf(1, [2, 4]));
        Assert.Equal(1, MapModeView.ClassOf(2, [2, 4]));
        Assert.Equal(2, MapModeView.ClassOf(9, [2, 4]));

        var data = TestData.Game;
        var growth = new MapModeView(data, Map, data.MapModes.Modes.First(m => m.Id == "growth"));
        Assert.Equal(data.MapModes.Palettes.DivergingNeutral, growth.ValueColor(0));
        Assert.Equal(data.MapModes.Palettes.DivergingNegative[0], growth.ValueColor(-20));
        Assert.Equal(data.MapModes.Palettes.DivergingPositive[^1], growth.ValueColor(20));
        Assert.Equal(data.MapModes.Palettes.NoData, growth.ValueColor(double.NaN));
        Assert.Equal(8, growth.Legend().Count); // 7 классов и «нет данных»
    }

    [Fact]
    public void EveryModeColorsEveryRegion()
    {
        var data = TestData.Game;
        var snapshot = WorldSnapshot.Capture(WorldFactory.Create(data), 1, true, 0);
        foreach (var mode in data.MapModes.Modes)
        {
            var view = new MapModeView(data, Map, mode);
            foreach (var region in Map.Regions)
                Assert.Matches("^#[0-9a-f]{6}$", view.ColorOf(region, snapshot));
        }
    }

    [Fact]
    public void OccupiedRegionTakesOccupierColor()
    {
        var data = TestData.Game;
        var world = WorldFactory.Create(data);
        var region = world.Regions.First(r => r.Owner == "FRA");
        world.TransferControl(region.Id, "DEU");
        var snapshot = WorldSnapshot.Capture(world, 1, true, 0);
        var political = new MapModeView(data, Map, data.MapModes.Modes.First(m => m.Kind == "political"));
        Assert.Equal(political.PoliticalColor("DEU"), political.ColorOf(Map.Region(region.Id)!, snapshot));
    }
}
