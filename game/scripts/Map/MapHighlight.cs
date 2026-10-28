using Godot;
using Politeconomy.Game.UI;
using Politeconomy.Sim.Geo;

namespace Politeconomy.Game.Map;

/// <summary>Контуры страны и региона под курсором и выбранных. Толщина линии постоянна на экране.</summary>
public partial class MapHighlight : Node2D
{
    readonly Dictionary<string, Vector2[][]> _regionRings = new(StringComparer.Ordinal);
    readonly Dictionary<string, Vector2[][]> _countryRings = new(StringComparer.Ordinal);

    public MapRegion? HoveredRegion { get; set; }
    public MapCountry? HoveredCountry { get; set; }
    public MapRegion? SelectedRegion { get; set; }
    public MapCountry? SelectedCountry { get; set; }
    public float Zoom { get; set; } = 1;

    public override void _Draw()
    {
        if (HoveredCountry != null && HoveredCountry != SelectedCountry)
            foreach (var ring in CountryRings(HoveredCountry))
                DrawPolyline(ring, new Color(1, 1, 1, 0.35f), 1.5f / Zoom, true);
        if (HoveredRegion != null && HoveredRegion != SelectedRegion)
            foreach (var ring in RegionRings(HoveredRegion))
                DrawPolyline(ring, new Color(1, 1, 1, 0.75f), 1.2f / Zoom, true);
        if (SelectedCountry != null)
            foreach (var ring in CountryRings(SelectedCountry))
                DrawPolyline(ring, UiTheme.Accent, 2.5f / Zoom, true);
        if (SelectedRegion != null)
            foreach (var ring in RegionRings(SelectedRegion))
                DrawPolyline(ring, UiTheme.AccentLight, 1.6f / Zoom, true);
    }

    Vector2[][] RegionRings(MapRegion region)
    {
        if (_regionRings.TryGetValue(region.Id, out var cached))
            return cached;
        var list = new List<Vector2[]>();
        foreach (var p in region.Polygons)
            for (var r = 0; r < p.RingCount; r++)
                list.Add(Closed(p.Ring(r)));
        return _regionRings[region.Id] = list.ToArray();
    }

    Vector2[][] CountryRings(MapCountry country)
    {
        if (_countryRings.TryGetValue(country.Id, out var cached))
            return cached;
        return _countryRings[country.Id] = country.Outline.Select(r => Closed(r)).ToArray();
    }

    static Vector2[] Closed(ReadOnlySpan<float> ring)
    {
        var n = ring.Length / 2;
        var pts = new Vector2[n + 1];
        for (var i = 0; i < n; i++)
            pts[i] = new Vector2(ring[i * 2], ring[i * 2 + 1]);
        pts[n] = pts[0];
        return pts;
    }
}
