using Godot;

namespace Politeconomy.Game.UI;

/// <summary>Левая колонка кнопок: экраны правительства, законов, бюджета. Горячие клавиши — в подсказках.</summary>
public partial class ScreenRail : GlassPanel
{
    readonly Dictionary<string, RailButton> _buttons = new();

    public event Action<string>? ScreenChosen;

    public void Setup(IEnumerable<(string Id, string Glyph, string Tooltip)> screens)
    {
        SetAnchorsPreset(LayoutPreset.TopLeft);
        OffsetLeft = 12;
        OffsetTop = 78;
        MouseFilter = MouseFilterEnum.Stop;
        InitGlass(radius: 14, padH: 6, padV: 6);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        Body.AddChild(column);
        foreach (var (id, glyph, tooltip) in screens)
        {
            var b = new RailButton { Glyph = glyph, TooltipText = tooltip };
            b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(1, 1, 1, 0), null, 22, 0, 0));
            b.AddThemeStyleboxOverride("hover", UiTheme.Box(UiTheme.ButtonHover, null, 22, 0, 0));
            b.AddThemeStyleboxOverride("pressed", UiTheme.Box(UiTheme.ButtonPressed, null, 22, 0, 0));
            b.AddThemeStyleboxOverride("hover_pressed", UiTheme.Box(UiTheme.ButtonPressed, null, 22, 0, 0));
            b.Pressed += () => ScreenChosen?.Invoke(id);
            column.AddChild(b);
            _buttons[id] = b;
        }
    }

    /// <summary>Подсветить кнопку открытого экрана (или ни одной).</summary>
    public void SetActive(string? id)
    {
        foreach (var (key, b) in _buttons)
        {
            b.SetPressedNoSignal(key == id);
            b.QueueRedraw();
        }
    }
}
