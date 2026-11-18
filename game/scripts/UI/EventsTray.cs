using Godot;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>События, которые ждут решения: кнопки под верхней панелью слева. Щелчок открывает окно события.</summary>
public partial class EventsTray : VBoxContainer
{
    public event Action<int>? Chosen;

    string _shown = "";

    public void Setup()
    {
        SetAnchorsPreset(LayoutPreset.TopLeft);
        OffsetLeft = 84;
        OffsetTop = 80;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 6);
    }

    public void Show(IReadOnlyList<PendingEventSnapshot>? pending)
    {
        var key = pending == null ? "" : string.Join("|", pending.Select(p => $"{p.Id}/{p.Deadline}"));
        if (key == _shown)
            return;
        _shown = key;
        foreach (var child in GetChildren())
            child.QueueFree();
        foreach (var e in pending ?? [])
        {
            var id = e.Id;
            var button = new Button
            {
                Text = $"  {e.Title} · решить до {Fmt.Date(e.Deadline)}", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34),
                Alignment = HorizontalAlignment.Left, TooltipText = "Событие ждёт вашего решения. Если не решить в срок, сработает вариант по умолчанию.",
            };
            button.AddThemeFontSizeOverride("font_size", 13);
            button.AddThemeColorOverride("font_color", UiTheme.TextPrimary);
            button.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.PanelRaised, UiTheme.Hex(e.Color), 10, 12, 6));
            button.AddThemeStyleboxOverride("hover", UiTheme.Box(UiTheme.PanelRaised, UiTheme.Accent, 10, 12, 6));
            button.Pressed += () => Chosen?.Invoke(id);
            AddChild(button);
        }
    }
}
