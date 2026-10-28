using Godot;

namespace Politeconomy.Game.UI;

/// <summary>
/// Панель из «стекла»: под ней размытая карта, сверху тёмный оттенок, скруглённые углы и мягкая тень.
/// Содержимое кладётся в <see cref="Body"/> — там уже есть отступы.
/// </summary>
public partial class GlassPanel : PanelContainer
{
    static Shader? _shader;
    ShaderMaterial _material = null!;

    public MarginContainer Body { get; private set; } = null!;

    protected void InitGlass(int radius = 14, int padH = 16, int padV = 14, float tint = 0.8f)
    {
        AddThemeStyleboxOverride("panel", UiTheme.Shadow(radius));
        _material = new ShaderMaterial { Shader = _shader ??= GD.Load<Shader>("res://shaders/glass.gdshader") };
        _material.SetShaderParameter("radius", (float)radius);
        _material.SetShaderParameter("tint", new Color(0.055f, 0.067f, 0.086f, tint));
        var glass = new ColorRect { Name = "Glass", MouseFilter = MouseFilterEnum.Ignore, Material = _material };
        AddChild(glass);
        Body = new MarginContainer { Name = "Body", MouseFilter = MouseFilterEnum.Ignore };
        Body.AddThemeConstantOverride("margin_left", padH);
        Body.AddThemeConstantOverride("margin_right", padH);
        Body.AddThemeConstantOverride("margin_top", padV);
        Body.AddThemeConstantOverride("margin_bottom", padV);
        AddChild(Body);
        Resized += () => _material.SetShaderParameter("size", Size);
    }

    Tween? _appear;

    /// <summary>
    /// Плавное появление: панель проявляется и чуть «вырастает» из точки <paramref name="pivot"/>
    /// (доли размера: (1, 0) — правый верхний угол). Раскладку это не трогает.
    /// </summary>
    protected void AnimateIn(Vector2 pivot, float seconds = 0.22f, float fromScale = 0.97f)
    {
        _appear?.Kill();
        PivotOffset = Size * pivot;
        Modulate = new Color(1, 1, 1, 0);
        Scale = new Vector2(fromScale, fromScale);
        _appear = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _appear.TweenProperty(this, "modulate:a", 1f, seconds);
        _appear.TweenProperty(this, "scale", Vector2.One, seconds);
    }
}
