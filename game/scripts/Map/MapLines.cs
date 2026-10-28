using Godot;

namespace Politeconomy.Game.Map;

/// <summary>Слой линий карты: границы, побережье, сетка. Толщина постоянна на экране, края сглажены.</summary>
public partial class MapLines : MeshInstance2D
{
    static Shader? _shader;
    ShaderMaterial _material = null!;

    public static MapLines Create(string name, IEnumerable<float[]> lines, float width, float feather, Color color,
        bool glow = false, float fadeFrom = 0, float fadeTo = 0, bool rings = false)
    {
        var layer = new MapLines { Name = name };
        layer.Mesh = LineMesh.Build(lines, rings);
        layer._material = new ShaderMaterial { Shader = _shader ??= GD.Load<Shader>("res://shaders/map_line.gdshader") };
        layer._material.SetShaderParameter("width", width);
        layer._material.SetShaderParameter("feather", feather);
        layer._material.SetShaderParameter("line_color", color);
        layer._material.SetShaderParameter("glow", glow);
        layer._material.SetShaderParameter("fade_from", fadeFrom);
        layer._material.SetShaderParameter("fade_to", fadeTo);
        layer.Material = layer._material;
        return layer;
    }


    public void SetZoom(float zoom) => _material.SetShaderParameter("zoom", zoom);

    public void SetColor(Color color) => _material.SetShaderParameter("line_color", color);

    public void SetWidth(float width) => _material.SetShaderParameter("width", width);
}
