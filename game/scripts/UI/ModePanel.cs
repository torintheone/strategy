using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Views;

namespace Politeconomy.Game.UI;

/// <summary>Левый нижний угол: легенда текущего режима карты и кнопки режимов.</summary>
public partial class ModePanel : GlassPanel
{
    Label _title = null!;
    VBoxContainer _legend = null!;
    readonly Dictionary<string, Button> _buttons = new();

    public event Action<MapModeDef>? ModeChosen;

    public void Setup(GameData data)
    {
        SetAnchorsPreset(LayoutPreset.BottomLeft);
        GrowVertical = GrowDirection.Begin;
        OffsetLeft = 12;
        OffsetBottom = -12;
        OffsetTop = -12;
        CustomMinimumSize = new Vector2(470, 0);
        MouseFilter = MouseFilterEnum.Stop;
        InitGlass(radius: 14, padH: 16, padV: 14);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        Body.AddChild(column);

        _title = UiTheme.Text("", 15, UiTheme.SemiBold);
        column.AddChild(_title);
        _legend = new VBoxContainer();
        _legend.AddThemeConstantOverride("separation", 3);
        column.AddChild(_legend);
        column.AddChild(new HSeparator());

        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        column.AddChild(flow);
        var group = new ButtonGroup();
        foreach (var mode in data.MapModes.Modes)
        {
            var b = new Button
            {
                Text = mode.ShortName,
                ToggleMode = true,
                ButtonGroup = group,
                FocusMode = FocusModeEnum.None,
                TooltipText = mode.Hotkey is null ? mode.Name : $"{mode.Name} ({mode.Hotkey})",
            };
            b.Pressed += () => ModeChosen?.Invoke(mode);
            flow.AddChild(b);
            _buttons[mode.Id] = b;
        }
    }

    public void Show(MapModeView view)
    {
        _title.Text = "Карта: " + view.Mode.Name;
        foreach (var (id, button) in _buttons)
            button.SetPressedNoSignal(id == view.Mode.Id);
        foreach (var child in _legend.GetChildren())
            child.QueueFree();

        var entries = view.Legend();
        if (entries.Count == 0)
        {
            _legend.AddChild(UiTheme.Text("Соседние страны окрашены в разные цвета.", 13, null, UiTheme.TextSecondary));
            _legend.AddChild(UiTheme.Text("Серым — территории с особым статусом.", 13, null, UiTheme.TextMuted));
            return;
        }
        foreach (var e in entries)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var swatch = new Panel { CustomMinimumSize = new Vector2(22, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            swatch.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Hex(e.Color), new Color(1, 1, 1, 0.1f), 4, 0, 0));
            row.AddChild(swatch);
            row.AddChild(UiTheme.Text(e.Text, 13, null, UiTheme.TextSecondary));
            _legend.AddChild(row);
        }
    }
}
