using Godot;
using Politeconomy.Game.UI;
using Politeconomy.Sim.Geo;

namespace Politeconomy.Game.Map;

/// <summary>Контур страны под курсором и выбранной страны. Толщина линии постоянна на экране.</summary>
public partial class MapHighlight : Node2D
{
    readonly Dictionary<string, Vector2[][]> _rings = new(StringComparer.Ordinal);

    public MapRegion? Hovered { get; set; }
    public MapRegion? Selected { get; set; }
    public float Zoom { get; set; } = 1;

    public override void _Draw()
    {
        if (Hovered != null && Hovered != Selected)
            foreach (var ring in Rings(Hovered))
                DrawPolyline(ring, new Color(1, 1, 1, 0.55f), 1.5f / Zoom, true);
        if (Selected != null)
            foreach (var ring in Rings(Selected))
                DrawPolyline(ring, UiTheme.Accent, 2.5f / Zoom, true);
    }

    Vector2[][] Rings(MapRegion region)
    {
        if (_rings.TryGetValue(region.Id, out var cached))
            return cached;
        var list = new List<Vector2[]>();
        foreach (var p in region.Polygons)
            for (var r = 0; r < p.RingCount; r++)
            {
                var ring = p.Ring(r);
                var n = ring.Length / 2;
                var pts = new Vector2[n + 1];
                for (var i = 0; i < n; i++)
                    pts[i] = new Vector2(ring[i * 2], ring[i * 2 + 1]);
                pts[n] = pts[0];
                list.Add(pts);
            }
        return _rings[region.Id] = list.ToArray();
    }
}
