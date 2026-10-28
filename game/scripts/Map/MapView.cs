using Godot;
using Politeconomy.Game.UI;
using Politeconomy.Sim.Geo;

namespace Politeconomy.Game.Map;

/// <summary>
/// Заливка регионов и линии границ. Каждый регион — отдельная сетка треугольников, цвет меняется через Modulate,
/// поэтому смена режима карты или захват региона ничего не перестраивает.
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
        // Сначала тонкие границы регионов, поверх — побережье и границы стран.
        AddChild(new MeshInstance2D { Mesh = LineMesh(map.InternalLines), Modulate = UiTheme.MapRegionBorder, Name = "RegionBorders" });
        AddChild(new MeshInstance2D { Mesh = LineMesh(map.CoastLines), Modulate = UiTheme.MapCoast, Name = "Coast" });
        AddChild(new MeshInstance2D { Mesh = LineMesh(map.BorderLines), Modulate = UiTheme.MapBorder, Name = "CountryBorders" });
        _highlight = new MapHighlight { Name = "Highlight" };
        AddChild(_highlight);
    }

    public void SetColor(string regionId, Color color)
    {
        if (_fills.TryGetValue(regionId, out var fill))
            fill.Modulate = color;
    }

    public void SetHighlight(MapRegion? hoveredRegion, MapCountry? hoveredCountry, MapRegion? selectedRegion, MapCountry? selectedCountry, float zoom)
    {
        _highlight.HoveredRegion = hoveredRegion;
        _highlight.HoveredCountry = hoveredCountry;
        _highlight.SelectedRegion = selectedRegion;
        _highlight.SelectedCountry = selectedCountry;
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

    /// <summary>Ломаные линии одной сеткой отрезков: на экране всегда толщиной в пиксель.</summary>
    static ArrayMesh LineMesh(IReadOnlyList<float[]> lines)
    {
        var points = new List<Vector2>();
        foreach (var line in lines)
            for (var i = 0; i + 3 < line.Length; i += 2)
            {
                points.Add(new Vector2(line[i], line[i + 1]));
                points.Add(new Vector2(line[i + 2], line[i + 3]));
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
