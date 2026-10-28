using Godot;
using Politeconomy.Game.Map;
using Politeconomy.Game.UI;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Geo;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Views;

namespace Politeconomy.Game;

/// <summary>
/// Корневой узел игры: загружает данные, запускает симуляцию в фоне, строит карту и интерфейс,
/// раздаёт им свежие снимки мира и обрабатывает ввод на карте.
/// </summary>
public partial class Main : Node
{
    const float ClickSlop = 5f;

    GameData _data = null!;
    WorldMap _map = null!;
    SimulationHost? _host;

    MapView _mapView = null!;
    MapCamera _camera = null!;
    MapOverlay _overlay = null!;
    TopBar _topBar = null!;
    ModePanel _modePanel = null!;
    CountryPanel _countryPanel = null!;
    HoverCard _hoverCard = null!;
    Control _uiRoot = null!;

    MapModeView _mode = null!;
    readonly Dictionary<string, Color> _colors = new(StringComparer.Ordinal);
    MapRegion? _hovered;
    MapRegion? _selected;
    long _shownVersion = -1;
    bool _dragging;
    bool _dragMoved;
    Vector2 _dragStart;
    Vector2 _mouse;

    // Дымовой тест для CI: запуск с аргументом «-- --smoke» проходит по всем режимам карты и выходит.
    bool _smoke;
    double _smokeTime;
    int _smokeStep;

    public override void _Ready()
    {
        _smoke = OS.GetCmdlineUserArgs().Contains("--smoke");
        var window = GetWindow();
        window.Title = "Politeconomy";
        window.MinSize = new Vector2I(1280, 720);

        var theme = UiTheme.Build();
        ThemeDB.GetDefaultTheme().MergeWith(theme);
        var dir = DataDirectory.Find(OS.GetExecutablePath().GetBaseDir(), ProjectSettings.GlobalizePath("res://"));
        if (dir == null)
        {
            ShowFatal("Не найдена папка data рядом с игрой. Распакуйте архив целиком и запустите Politeconomy.exe из распакованной папки.", theme);
            return;
        }
        try
        {
            _data = GameData.Load(dir);
        }
        catch (DataException e)
        {
            ShowFatal("Не удалось прочитать данные игры.\n" + e.Message, theme);
            return;
        }

        _map = WorldMap.From(_data.Map);
        var world = WorldFactory.Create(_data);
        _host = new SimulationHost(new Simulation(world), _data.Balance.Time.DaysPerSecond);

        BuildMap();
        BuildInterface(theme);
        SetMode(_data.MapModes.Modes[0]);
        _host.Start();
    }

    void BuildMap()
    {
        _mapView = new MapView { Name = "Map" };
        AddChild(_mapView);
        _mapView.Build(_map);

        _camera = new MapCamera { Name = "Camera" };
        AddChild(_camera);
        _camera.MakeCurrent();
        _camera.Setup(new Rect2(_map.MinX, _map.MinY, _map.Width, _map.Height));
        _camera.Changed += OnCameraChanged;

        var overlayLayer = new CanvasLayer { Layer = 1, Name = "MapLabels" };
        AddChild(overlayLayer);
        _overlay = new MapOverlay();
        overlayLayer.AddChild(_overlay);
        _overlay.Setup(_map, _data);
    }

    void BuildInterface(Theme theme)
    {
        var layer = new CanvasLayer { Layer = 10, Name = "Interface" };
        AddChild(layer);
        _uiRoot = new Control { Theme = theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        _uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_uiRoot);

        _topBar = new TopBar();
        _uiRoot.AddChild(_topBar);
        _topBar.Setup(_data);
        _topBar.PauseToggled += () => _host!.TogglePause();
        _topBar.SpeedChosen += speed => _host!.SetSpeed(speed);
        _topBar.PlayerClicked += () =>
        {
            if (_host!.Snapshot.PlayerCountry is { } id)
                Select(_map.ById(id), center: true);
        };

        _modePanel = new ModePanel();
        _uiRoot.AddChild(_modePanel);
        _modePanel.Setup(_data);
        _modePanel.ModeChosen += SetMode;

        _countryPanel = new CountryPanel { Visible = false };
        _uiRoot.AddChild(_countryPanel);
        _countryPanel.Setup(_data);
        _countryPanel.Closed += () => Select(null);
        _countryPanel.PlayAs += id => _host!.Post(w => w.PlayerCountry = id);

        _hoverCard = new HoverCard();
        _uiRoot.AddChild(_hoverCard);
        _hoverCard.Setup();

        // Когда курсор уходит на панель, карта не должна подсвечивать страну под ней.
        foreach (var panel in new Control[] { _topBar, _modePanel, _countryPanel })
            panel.MouseEntered += () => SetHovered(null);
    }

    public override void _Process(double delta)
    {
        if (_smoke && _host != null)
            SmokeStep(delta);
        if (_host == null)
            return;
        if (_host.Error is { } error)
        {
            GD.PushError(error.ToString());
            ShowFatal("Ошибка в симуляции:\n" + error.Message, _uiRoot.Theme);
            _host.Dispose();
            _host = null;
            return;
        }
        var snapshot = _host.Snapshot;
        if (snapshot.Version == _shownVersion)
            return;
        _shownVersion = snapshot.Version;
        _topBar.Show(snapshot);
        Recolor(snapshot);
        if (_countryPanel.Visible)
            _countryPanel.Refresh(snapshot);
    }

    void SetMode(MapModeDef mode)
    {
        _mode = new MapModeView(_data, mode);
        _modePanel.Show(_mode);
        if (_host != null)
            Recolor(_host.Snapshot);
        UpdateHoverCard();
    }

    void Recolor(WorldSnapshot snapshot)
    {
        var changed = false;
        foreach (var region in _map.Regions)
        {
            var color = UiTheme.Hex(_mode.ColorOf(region, snapshot.Countries.GetValueOrDefault(region.Id)));
            if (_colors.TryGetValue(region.Id, out var old) && old == color)
                continue;
            _colors[region.Id] = color;
            _mapView.SetColor(region.Id, color);
            changed = true;
        }
        if (!changed)
            return;
        _overlay.Colors = _colors;
        _overlay.QueueRedraw();
    }

    void OnCameraChanged()
    {
        _overlay.Zoom = _camera.ZoomLevel;
        _overlay.QueueRedraw();
        _mapView.SetHighlight(_hovered, _selected, _camera.ZoomLevel);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_host == null)
            return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheel:
                _camera.ZoomAt(wheel.Position, 1.15f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheel:
                _camera.ZoomAt(wheel.Position, 1 / 1.15f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Middle or MouseButton.Right } button:
                if (button.Pressed)
                {
                    _dragging = true;
                    _dragMoved = false;
                    _dragStart = button.Position;
                }
                else
                {
                    if (!_dragMoved && button.ButtonIndex == MouseButton.Left)
                        Select(RegionAt(button.Position));
                    _dragging = false;
                }
                break;
            case InputEventMouseMotion motion:
                _mouse = motion.Position;
                if (_dragging)
                {
                    if (!_dragMoved && (motion.Position - _dragStart).Length() > ClickSlop)
                        _dragMoved = true;
                    if (_dragMoved)
                        _camera.PanByScreen(motion.Relative);
                }
                SetHovered(RegionAt(motion.Position));
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key.Keycode);
                break;
        }
    }

    void HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space:
                _host!.TogglePause();
                return;
            case >= Key.Key1 and <= Key.Key5:
                _host!.SetSpeed((int)(key - Key.Key1) + 1);
                return;
            case Key.Escape:
                Select(null);
                return;
            case Key.Equal or Key.KpAdd:
                _camera.ZoomAt(_camera.GetViewportRect().Size / 2, 1.3f);
                return;
            case Key.Minus or Key.KpSubtract:
                _camera.ZoomAt(_camera.GetViewportRect().Size / 2, 1 / 1.3f);
                return;
            case Key.Home:
                _camera.FitWorld();
                return;
        }
        foreach (var mode in _data.MapModes.Modes)
            if (mode.Hotkey != null && Enum.TryParse<Key>(mode.Hotkey, out var hotkey) && hotkey == key)
                SetMode(mode);
    }

    MapRegion? RegionAt(Vector2 screen)
    {
        var world = _camera.ScreenToWorld(screen);
        var radius = (MapOverlay.MarkerRadius + 3) / _camera.ZoomLevel;
        var region = _map.FindAt(world.X, world.Y, radius);
        // Метка действует только для стран, которые сейчас нарисованы кружком.
        if (region != null && !region.Contains(world.X, world.Y) && !_overlay.IsMarker(region))
            return null;
        return region;
    }

    void SetHovered(MapRegion? region)
    {
        if (region == _hovered)
        {
            UpdateHoverCard();
            return;
        }
        _hovered = region;
        _overlay.Hovered = region?.Id;
        _overlay.QueueRedraw();
        _mapView.SetHighlight(_hovered, _selected, _camera.ZoomLevel);
        UpdateHoverCard();
    }

    void UpdateHoverCard()
    {
        if (_hoverCard == null)
            return;
        if (_hovered == null || _host == null)
        {
            _hoverCard.Visible = false;
            return;
        }
        if (!_hovered.IsCountry)
        {
            _hoverCard.ShowAt(_mouse, _hovered.Name ?? _hovered.Id, _hovered.Note ?? "");
            return;
        }
        var name = _data.Countries.Countries.First(c => c.Id == _hovered.Id).Name;
        var country = _host.Snapshot.Countries[_hovered.Id];
        _hoverCard.ShowAt(_mouse, name, _mode.ValueText(country));
    }

    void Select(MapRegion? region, bool center = false)
    {
        if (region is { IsCountry: false })
            region = null;
        _selected = region;
        _overlay.Selected = region?.Id;
        _overlay.QueueRedraw();
        _mapView.SetHighlight(_hovered, _selected, _camera.ZoomLevel);
        if (region == null || _host == null)
        {
            _countryPanel.Visible = false;
            return;
        }
        _countryPanel.ShowCountry(region.Id, _host.Snapshot);
        if (center)
            _camera.CenterOn(new Vector2(region.LabelX, region.LabelY));
    }

    void SmokeStep(double delta)
    {
        _smokeTime += delta;
        switch (_smokeStep)
        {
            case 0 when _smokeTime > 0.3:
                _host!.SetSpeed(SimulationHost.MaxSpeed);
                Select(_map.ById("RUS"), center: true);
                _host.Post(w => w.PlayerCountry = "RUS");
                _smokeStep++;
                break;
            case 1:
                foreach (var mode in _data.MapModes.Modes)
                    SetMode(mode);
                foreach (var factor in new[] { 2f, 4f, 8f, 0.1f })
                {
                    _camera.ZoomAt(_camera.GetViewportRect().Size / 2, factor);
                    _overlay.QueueRedraw();
                }
                foreach (var region in _map.Regions)
                    SetHovered(region);
                Select(_map.ById("TUV"));
                _smokeStep++;
                break;
            case 2 when _smokeTime > 3:
                var snapshot = _host!.Snapshot;
                var ok = snapshot.Date > _data.Scenario.StartDate && snapshot.PlayerCountry == "RUS" && _host.Error == null;
                GD.Print($"SMOKE {(ok ? "OK" : "FAIL")}: дата {snapshot.Date:yyyy-MM-dd}, стран {snapshot.Countries.Count}, шаг {_host.LastStepMs:F2} мс");
                GetTree().Quit(ok ? 0 : 1);
                _smokeStep++;
                break;
        }
    }

    void ShowFatal(string message, Theme theme)
    {
        if (_smoke)
        {
            GD.PrintErr("SMOKE FAIL: " + message);
            GetTree().Quit(1);
        }
        var layer = new CanvasLayer { Layer = 100 };
        AddChild(layer);
        var panel = new PanelContainer { Theme = theme };
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        layer.AddChild(panel);
        var label = UiTheme.Text(message, 16, null, UiTheme.TextPrimary);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(560, 0);
        panel.AddChild(label);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
    }

    public override void _ExitTree() => _host?.Dispose();
}
