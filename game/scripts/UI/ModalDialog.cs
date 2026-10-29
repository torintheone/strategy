using Godot;

namespace Politeconomy.Game.UI;

/// <summary>Окно поверх игры: затемнение и панель по центру — заголовок, подзаголовок, содержимое и кнопки справа внизу.</summary>
public partial class ModalDialog : Control
{
    protected Label Title = null!;
    protected Label Subtitle = null!;
    protected VBoxContainer Content = null!;
    HBoxContainer _buttons = null!;

    protected void InitDialog(float width)
    {
        // Именно «с отступами»: окно уже на сцене, и простая привязка сохранила бы его нулевой размер.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.04f, 0.62f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.PanelRaised, UiTheme.PanelBorder, 14, 24, 20));
        center.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);
        var titles = new VBoxContainer();
        titles.AddThemeConstantOverride("separation", 2);
        column.AddChild(titles);
        Title = UiTheme.Text("", 22, UiTheme.SerifBold);
        Title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titles.AddChild(Title);
        Subtitle = UiTheme.Text("", 13, null, UiTheme.TextMuted);
        Subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titles.AddChild(Subtitle);
        Content = new VBoxContainer();
        Content.AddThemeConstantOverride("separation", 6);
        column.AddChild(Content);
        _buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(_buttons);
    }

    protected void ClearContent()
    {
        foreach (var child in Content.GetChildren())
            child.QueueFree();
    }

    protected void ClearButtons()
    {
        foreach (var child in _buttons.GetChildren())
            child.QueueFree();
    }

    /// <summary>Кнопка внизу; главная (primary) — золотая.</summary>
    protected Button AddButton(string text, bool primary, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) };
        if (primary)
        {
            button.AddThemeColorOverride("font_color", UiTheme.Ocean);
            button.AddThemeColorOverride("font_hover_color", UiTheme.Ocean);
            button.AddThemeColorOverride("font_disabled_color", UiTheme.TextMuted);
            button.AddThemeFontOverride("font", UiTheme.SemiBold);
            button.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.Accent, null, 10, 16, 8));
            button.AddThemeStyleboxOverride("hover", UiTheme.Box(UiTheme.AccentLight, null, 10, 16, 8));
            button.AddThemeStyleboxOverride("disabled", UiTheme.Box(UiTheme.ButtonBg, null, 10, 16, 8));
        }
        button.Pressed += pressed;
        _buttons.AddChild(button);
        return button;
    }

    protected static Label Line(string text, int size = 14, Color? color = null, Font? font = null)
    {
        var label = UiTheme.Text(text, size, font, color ?? UiTheme.TextSecondary);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    protected static Control Dot(string color)
    {
        var dot = new Panel { CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        dot.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Hex(color), null, 5, 0, 0));
        return dot;
    }

    /// <summary>Ячейка таблицы: число справа фиксированной ширины.</summary>
    protected static Label Cell(string text, float width, Color? color = null, Font? font = null)
    {
        var label = UiTheme.Text(text, 14, font ?? UiTheme.Numbers, color ?? UiTheme.TextPrimary);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.CustomMinimumSize = new Vector2(width, 0);
        return label;
    }
}
