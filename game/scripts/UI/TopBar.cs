using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>Верхняя панель: дата, пауза и скорость, ключевые показатели страны игрока.</summary>
public partial class TopBar : PanelContainer
{
    static readonly string[] KeyStats = ["gdp_growth_pct", "inflation_pct", "unemployment_pct", "gov_debt_pct_gdp"];

    GameData _data = null!;
    Label _date = null!;
    Label _player = null!;
    PlayButton _play = null!;
    readonly Button[] _speeds = new Button[SimulationHost.MaxSpeed];
    readonly Dictionary<string, (Control Box, Label Value)> _chips = new();

    public event Action? PauseToggled;
    public event Action<int>? SpeedChosen;
    public event Action? PlayerClicked;

    public void Setup(GameData data)
    {
        _data = data;
        SetAnchorsPreset(LayoutPreset.TopWide);
        OffsetBottom = 52;
        MouseFilter = MouseFilterEnum.Stop;
        var style = UiTheme.Box(UiTheme.Panel, null, 0, 16, 6);
        style.BorderColor = UiTheme.PanelBorder;
        style.BorderWidthBottom = 1;
        AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        AddChild(row);

        var title = UiTheme.Text("POLITECONOMY", 14, UiTheme.SemiBold, UiTheme.Accent);
        title.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(title);
        row.AddChild(new VSeparator());

        _date = UiTheme.Text("", 17, UiTheme.Medium);
        _date.CustomMinimumSize = new Vector2(190, 0);
        _date.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_date);

        _play = new PlayButton { TooltipText = "Пауза и продолжение (пробел)" };
        _play.Pressed += () => PauseToggled?.Invoke();
        row.AddChild(_play);
        for (var i = 0; i < _speeds.Length; i++)
        {
            var speed = i + 1;
            var b = new Button
            {
                Text = speed.ToString(),
                ToggleMode = true,
                CustomMinimumSize = new Vector2(30, 0),
                TooltipText = $"Скорость {speed}: {Fmt.Number(data.Balance.Time.DaysPerSecond[i], 0)} дн. в секунду (клавиша {speed})",
                FocusMode = FocusModeEnum.None,
            };
            b.Pressed += () => SpeedChosen?.Invoke(speed);
            _speeds[i] = b;
            row.AddChild(b);
        }

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        _player = UiTheme.Text("", 15, UiTheme.Medium);
        _player.VerticalAlignment = VerticalAlignment.Center;
        _player.MouseFilter = MouseFilterEnum.Stop;
        _player.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                PlayerClicked?.Invoke();
        };
        row.AddChild(_player);
        row.AddChild(new VSeparator());

        foreach (var id in KeyStats)
            AddChip(row, id, data.Indicator(id).Name);
        AddChip(row, "budget", "Бюджет");
        AddChip(row, "approval", "Одобрение");
        _chips["budget"].Box.TooltipText = "Сальдо бюджета появится на этапе 1, когда заработает модель финансов.";
        _chips["approval"].Box.TooltipText = "Одобрение власти появится на этапе 4, когда заработает политика.";
    }

    void AddChip(HBoxContainer row, string id, string caption)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(84, 0) };
        box.AddThemeConstantOverride("separation", -2);
        box.AddChild(UiTheme.Text(caption, 11, UiTheme.Regular, UiTheme.TextMuted));
        var value = UiTheme.Text("—", 15, UiTheme.Medium);
        box.AddChild(value);
        row.AddChild(box);
        _chips[id] = (box, value);
    }

    public void Show(WorldSnapshot snapshot)
    {
        _date.Text = Fmt.Date(snapshot.Date);
        _play.Paused = snapshot.Paused;
        for (var i = 0; i < _speeds.Length; i++)
            _speeds[i].SetPressedNoSignal(i + 1 == snapshot.Speed);

        var player = snapshot.PlayerCountry is { } id ? snapshot.Countries.GetValueOrDefault(id) : null;
        if (player == null)
        {
            _player.Text = "Выберите страну на карте";
            _player.AddThemeColorOverride("font_color", UiTheme.TextMuted);
            _player.TooltipText = "Нажмите на страну, затем «Играть за эту страну»";
        }
        else
        {
            _player.Text = _data.Countries.Countries.First(c => c.Id == player.Id).Name;
            _player.AddThemeColorOverride("font_color", UiTheme.TextPrimary);
            _player.TooltipText = "Ваша страна. Нажмите, чтобы открыть её досье.";
        }

        foreach (var stat in KeyStats)
        {
            var (box, value) = _chips[stat];
            var def = _data.Indicator(stat);
            var s = player?.Stat(stat);
            value.Text = s == null ? "—" : (s.IsEstimate ? "≈" : "") + Fmt.Value(def.Format == "percent_gdp" ? "percent" : def.Format, s.Value);
            box.TooltipText = player == null ? def.Description : Explain.Stat(def, s);
        }
    }
}
