using Godot;

namespace Politeconomy.Game.UI;

/// <summary>Одно уведомление: появляется, держится и тает.</summary>
public partial class Toast : GlassPanel
{
    public void Setup(string text, double lifetime)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        InitGlass(radius: 10, padH: 14, padV: 9, tint: 0.92f);
        var label = UiTheme.Text(text, 14, null, UiTheme.TextPrimary);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(520, 0);
        label.MouseFilter = MouseFilterEnum.Ignore;
        Body.AddChild(label);
        AnimateIn(new Vector2(0.5f, 0), 0.25f, 0.96f);
        var fade = CreateTween();
        fade.TweenInterval(lifetime);
        fade.TweenProperty(this, "modulate:a", 0f, 0.6f);
        fade.TweenCallback(Callable.From(QueueFree));
    }
}
