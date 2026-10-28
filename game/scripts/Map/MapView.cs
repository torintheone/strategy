using Godot;
using Politeconomy.Game.UI;
using Politeconomy.Sim.Geo;

namespace Politeconomy.Game.Map;

/// <summary>
/// Карта в мировых координатах: океан внутри контура проекции, сетка меридианов, свечение побережья,
/// заливка регионов и сглаженные линии границ. Каждый регион — отдельная сетка треугольников, цвет меняется
/// через Modulate и плавно перетекает при смене режима карты или захвате региона.
/// </summary>
public partial class MapView : Node2D
{
    const float ColorFadeSeconds = 0.35f;

    readonly Dictionary<string, MeshInstance2D> _fills = new(StringComparer.Ordinal);
    readonly Dictionary<string, (Color From, Color To, float T)> _fades = new(StringComparer.Ordinal);
    readonly List<MapLines> _lines = [];
    MapHighlight _highlight = null!;

    public void Build(WorldMap map)
    {
        var outline = EqualEarth.Outline(map.Scale);
        var ocean = new MeshInstance2D { Name = "Ocean", Mesh = FanMesh(outline) };
        var oceanMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/map_ocean.gdshader") };
        oceanMaterial.SetShaderParameter("half_size", new Vector2(map.Width / 2, map.Height / 2));
        ocean.Material = oceanMaterial;
        AddChild(ocean);

        AddLines(MapLines.Create("Graticule", EqualEarth.Graticule(map.Scale), 1f, 1f, UiTheme.MapGraticule));
        AddLines(MapLines.Create("RimGlow", [outline], 0f, 22f, UiTheme.MapRimGlow, glow: true, rings: true));
        AddLines(MapLines.Create("Rim", [outline], 1.2f, 1f, UiTheme.MapRim, rings: true));
        AddLines(MapLines.Create("CoastGlow", map.CoastLines, 0f, 12f, UiTheme.MapCoastGlow, glow: true));

        var land = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/map_land.gdshader") };
        land.SetShaderParameter("map_height", map.Height);
        var fills = new Node2D { Name = "Regions" };
        AddChild(fills);
        foreach (var region in map.Regions)
        {
            // Прозрачный до первой раскраски: первый цвет ставится сразу, следующие — плавным переходом.
            var fill = new MeshInstance2D { Mesh = FillMesh(region), Name = region.Id, Material = land, Modulate = new Color(1, 1, 1, 0) };
            fills.AddChild(fill);
            _fills[region.Id] = fill;
        }

        // Границы регионов проявляются при приближении; поверх — побережье и границы стран.
        AddLines(MapLines.Create("RegionBorders", map.InternalLines, 0.8f, 0.9f, UiTheme.MapRegionBorder, fadeFrom: 0.3f, fadeTo: 0.75f));
        AddLines(MapLines.Create("Coast", map.CoastLines, 1.1f, 0.9f, UiTheme.MapCoast));
        AddLines(MapLines.Create("CountryBorders", map.BorderLines, 1.5f, 1f, UiTheme.MapBorder));

        _highlight = new MapHighlight { Name = "Highlight" };
        AddChild(_highlight);
        _highlight.Setup(id => _fills.TryGetValue(id, out var f) ? f.Mesh : null);
    }

    void AddLines(MapLines layer)
    {
        AddChild(layer);
        _lines.Add(layer);
    }

    /// <summary>Новый цвет региона: плавный переход за доли секунды или сразу.</summary>
    public void SetColor(string regionId, Color color, bool animate = true)
    {
        if (!_fills.TryGetValue(regionId, out var fill))
            return;
        if (!animate || fill.Modulate.A == 0)
        {
            _fades.Remove(regionId);
            fill.Modulate = color;
            return;
        }
        _fades[regionId] = (fill.Modulate, color, 0);
    }

    public void SetZoom(float zoom)
    {
        foreach (var layer in _lines)
            layer.SetZoom(zoom);
        _highlight.SetZoom(zoom);
    }

    public void SetHighlight(MapRegion? hoveredRegion, MapCountry? hoveredCountry, MapRegion? selectedRegion, MapCountry? selectedCountry) =>
        _highlight.Set(hoveredRegion, hoveredCountry, selectedRegion, selectedCountry);

    public override void _Process(double delta)
    {
        if (_fades.Count == 0)
            return;
        foreach (var id in _fades.Keys.ToList())
        {
            var (from, to, t) = _fades[id];
            t = Mathf.Min(1, t + (float)delta / ColorFadeSeconds);
            var eased = 1 - (1 - t) * (1 - t) * (1 - t);
            _fills[id].Modulate = from.Lerp(to, eased);
            if (t >= 1)
                _fades.Remove(id);
            else
                _fades[id] = (from, to, t);
        }
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
        return BuildMesh(vertices, indices);
    }

    /// <summary>Выпуклый контур проекции — веером треугольников от центра.</summary>
    static ArrayMesh FanMesh(float[] ring)
    {
        var n = ring.Length / 2;
        var vertices = new Vector2[n + 1];
        vertices[0] = Vector2.Zero;
        for (var i = 0; i < n; i++)
            vertices[i + 1] = new Vector2(ring[i * 2], ring[i * 2 + 1]);
        var indices = new List<int>();
        for (var i = 1; i <= n; i++)
        {
            indices.Add(0);
            indices.Add(i);
            indices.Add(i % n + 1);
        }
        return BuildMesh(vertices, indices.ToArray());
    }

    static ArrayMesh BuildMesh(Vector2[] vertices, int[] indices)
    {
        var colors = new Color[vertices.Length];
        Array.Fill(colors, Colors.White);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
