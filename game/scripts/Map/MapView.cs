using Godot;
using Politeconomy.Game.UI;
using Politeconomy.Sim.Geo;

namespace Politeconomy.Game.Map;

/// <summary>
/// Заливка стран и границы. Каждая страна — отдельная сетка треугольников, цвет меняется через Modulate,
/// поэтому смена режима карты ничего не перестраивает.
/// </summary>
public partial class MapView : Node2D
{
    readonly Dictionary<string, MeshInstance2D> _fills = new(StringComparer.Ordinal);
    MapHighlight _highlight = null!;

    public void Build(WorldMap map)
    {
        foreach (var region in map.Regions)
        {
            var fill = new MeshInstance2D { Mesh = FillMesh(region), Name = region.Id };
            AddChild(fill);
            _fills[region.Id] = fill;
        }
        AddChild(new MeshInstance2D { Mesh = BorderMesh(map), Modulate = UiTheme.MapBorder, Name = "Borders" });
        _highlight = new MapHighlight { Name = "Highlight" };
        AddChild(_highlight);
    }

    public void SetColor(string id, Color color)
    {
        if (_fills.TryGetValue(id, out var fill))
            fill.Modulate = color;
    }

    public void SetHighlight(MapRegion? hovered, MapRegion? selected, float zoom)
    {
        _highlight.Hovered = hovered;
        _highlight.Selected = selected;
        _highlight.Zoom = zoom;
        _highlight.QueueRedraw();
    }

    static ArrayMesh FillMesh(MapRegion region)
    {
        var vertexCount = region.Polygons.Sum(p => p.Vertices.Length / 2);
        var indexCount = region.Polygons.Sum(p => p.Triangles.Length);
        var vertices = new Vector2[vertexCount];
        var indices = new int[indexCount];
        int v = 0, k = 0;
        foreach (var p in region.Polygons)
        {
            var offset = v;
            for (var i = 0; i < p.Vertices.Length; i += 2)
                vertices[v++] = new Vector2(p.Vertices[i], p.Vertices[i + 1]);
            foreach (var t in p.Triangles)
                indices[k++] = offset + t;
        }
        var colors = new Color[vertexCount];
        Array.Fill(colors, Colors.White);
        return BuildMesh(Godot.Mesh.PrimitiveType.Triangles, vertices, colors, indices);
    }

    static ArrayMesh BorderMesh(WorldMap map)
    {
        var points = new List<Vector2>();
        foreach (var region in map.Regions)
            foreach (var p in region.Polygons)
                for (var r = 0; r < p.RingCount; r++)
                {
                    var ring = p.Ring(r);
                    var n = ring.Length / 2;
                    for (var i = 0; i < n; i++)
                    {
                        var j = (i + 1) % n;
                        points.Add(new Vector2(ring[i * 2], ring[i * 2 + 1]));
                        points.Add(new Vector2(ring[j * 2], ring[j * 2 + 1]));
                    }
                }
        var colors = new Color[points.Count];
        Array.Fill(colors, Colors.White);
        return BuildMesh(Godot.Mesh.PrimitiveType.Lines, points.ToArray(), colors, null);
    }

    static ArrayMesh BuildMesh(Godot.Mesh.PrimitiveType type, Vector2[] vertices, Color[] colors, int[]? indices)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Godot.Mesh.ArrayType.Color] = colors;
        if (indices != null)
            arrays[(int)Godot.Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(type, arrays);
        return mesh;
    }
}
