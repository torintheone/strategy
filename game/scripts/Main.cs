using Godot;
using Politeconomy.Game.Map;
using Politeconomy.Game.UI;
using Politeconomy.Sim;
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
    Dictionary<string, RegionRecord> _regionInfo = null!;
    Dictionary<string, string> _countryNames = null!;

    MapView _mapView = null!;
    MapCamera _camera = null!;
    MapOverlay _overlay = null!;
    TopBar _topBar = null!;
    ModePanel _modePanel = null!;
    CountryPanel _countryPanel = null!;
    HoverCard _hoverCard = null!;
    ScreenRail _rail = null!;
    GovernmentScreen _government = null!;
    LawsScreen _laws = null!;
    Toasts _toasts = null!;
    Control _uiRoot = null!;

    MapModeView _mode = null!;
    readonly Dictionary<string, Color> _regionColors = new(StringComparer.Ordinal);
    readonly Dictionary<string, Color> _countryColors = new(StringComparer.Ordinal);
    MapRegion? _hoveredRegion;
    MapCountry? _hoveredCountry;
    MapRegion? _selectedRegion;
    MapCountry? _selectedCountry;
    long _shownVersion = -1;
    bool _dragging;
    bool _dragMoved;
    Vector2 _dragStart;
    Vector2 _mouse;

    // Скриншоты для CI: «-- --screenshots <папка>» снимает несколько видов карты и интерфейса и выходит.
    string? _shotsDir;
    int _shotStep;
    int _shotFrames;

    // Дымовой тест для CI: запуск с аргументом «-- --smoke» проходит по всем режимам карты и выходит.
    bool _smoke;
    double _smokeTime;
    int _smokeStep;
    string? _smokeOccupied;

    public override void _Ready()
    {
        var userArgs = OS.GetCmdlineUserArgs();
        _smoke = userArgs.Contains("--smoke");
        var shots = Array.IndexOf(userArgs, "--screenshots");
        if (shots >= 0 && shots + 1 < userArgs.Length)
            _shotsDir = userArgs[shots + 1];
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
        _regionInfo = _data.Regions.Regions.ToDictionary(r => r.Id, StringComparer.Ordinal);
        _countryNames = _data.Countries.Countries.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        // Новая партия: мир, экономика по данным и разогрев модели (меньше секунды).
        var start = GameSetup.NewGame(_data);
        _host = new SimulationHost(start.Simulation, _data.Balance.Time.DaysPerSecond);

        BuildMap();
        BuildInterface(theme);
        SetMode(_data.MapModes.Modes[0]);
        _host.Start();
    }

    void BuildMap()
    {
        // Фон за пределами карты мира — экранный, с виньеткой.
        var backdrop = new CanvasLayer { Layer = -10, Name = "Backdrop" };
        AddChild(backdrop);
        var back = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
        back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        back.Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/backdrop.gdshader") };
        backdrop.AddChild(back);

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
        _mapView.SetZoom(_camera.ZoomLevel);
        _overlay.Zoom = _camera.ZoomLevel;
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
                SelectCountry(_map.Country(id), center: true);
        };

        _modePanel = new ModePanel();
        _uiRoot.AddChild(_modePanel);
        _modePanel.Setup(_data);
        _modePanel.ModeChosen += SetMode;

        _countryPanel = new CountryPanel { Visible = false };
        _uiRoot.AddChild(_countryPanel);
        _countryPanel.Setup(_data);
        _countryPanel.Closed += () => SelectRegion(null);
        _countryPanel.PlayAs += id => _host!.Post(w => w.PlayerCountry = id);
        _countryPanel.RegionChosen += id => SelectRegion(_map.Region(id), center: true);

        _countryPanel.GovernmentRequested += id => OpenGovernment(id);

        _rail = new ScreenRail();
        _uiRoot.AddChild(_rail);
        _rail.Setup([("government", "government", "Правительство и парламент (G)"), ("laws", "laws", "Законы (L)")]);
        _rail.ScreenChosen += ToggleScreen;

        _government = new GovernmentScreen { Visible = false };
        _uiRoot.AddChild(_government);
        _government.Setup(_data);
        _government.Closed += CloseScreens;
        _government.CandidatesRequested += (country, post) =>
            _host!.Post(w => Politeconomy.Sim.Government.GovernmentActions.RequestCandidates(w, country, post));
        _government.Appointed += (country, post, index) =>
            _host!.Post(w => Politeconomy.Sim.Government.GovernmentActions.Appoint(w, country, post, index));

        _laws = new LawsScreen { Visible = false };
        _uiRoot.AddChild(_laws);
        _laws.Setup(_data);
        _laws.Closed += CloseScreens;
        _laws.Proposed += (country, law, variant) =>
            _host!.Post(w => Politeconomy.Sim.Laws.LawActions.Propose(w, country, law, variant));
        _laws.Withdrawn += (country, law) =>
            _host!.Post(w => Politeconomy.Sim.Laws.LawActions.Withdraw(w, country, law));

        _toasts = new Toasts();
        _uiRoot.AddChild(_toasts);
        _toasts.Setup();

        _hoverCard = new HoverCard();
        _uiRoot.AddChild(_hoverCard);
        _hoverCard.Setup();

        // Когда курсор уходит на панель, карта не должна подсвечивать регион под ней.
        foreach (var panel in new Control[] { _topBar, _modePanel, _countryPanel, _rail, _government, _laws })
            panel.MouseEntered += () => SetHovered(null, null);
    }

    public override void _Process(double delta)
    {
        if (_smoke && _host != null)
            SmokeStep(delta);
        if (_shotsDir != null && _host != null)
            ScreenshotStep();
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
        _toasts.Feed(snapshot.Journal, snapshot.PlayerCountry, silent: _shownVersion < 0);
        _shownVersion = snapshot.Version;
        _topBar.Show(snapshot);
        if (_government.Visible)
            _government.Refresh(snapshot);
        if (_laws.Visible)
            _laws.Refresh(snapshot);
        Recolor(snapshot);
        if (_countryPanel.Visible)
            _countryPanel.Refresh(snapshot);
    }

    void SetMode(MapModeDef mode)
    {
        _mode = new MapModeView(_data, _map, mode);
        _modePanel.Show(_mode);
        if (_host != null)
            Recolor(_host.Snapshot);
        UpdateHoverCard();
    }

    /// <summary>Перекрашивает только те регионы, у которых цвет действительно изменился.</summary>
    void Recolor(WorldSnapshot snapshot)
    {
        var changed = false;
        foreach (var region in _map.Regions)
        {
            var color = UiTheme.Hex(_mode.ColorOf(region, snapshot));
            if (_regionColors.TryGetValue(region.Id, out var old) && old == color)
                continue;
            _regionColors[region.Id] = color;
            _mapView.SetColor(region.Id, color);
            changed = true;
        }
        if (!changed)
            return;
        // Метка мелкой страны берёт цвет её самого большого региона.
        foreach (var country in _map.Countries)
            _countryColors[country.Id] = _regionColors[country.Regions.MaxBy(r => r.Area)!.Id];
        _overlay.CountryColors = _countryColors;
        _overlay.QueueRedraw();
    }

    void OnCameraChanged()
    {
        _overlay.Zoom = _camera.ZoomLevel;
        _overlay.QueueRedraw();
        _mapView.SetZoom(_camera.ZoomLevel);
    }

    void UpdateHighlight() =>
        _mapView.SetHighlight(_hoveredRegion, _hoveredCountry, _selectedRegion, _selectedCountry);

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
                    _camera.StartDrag();
                }
                else
                {
                    if (!_dragMoved && button.ButtonIndex == MouseButton.Left)
                        ClickAt(button.Position);
                    else if (_dragMoved)
                        _camera.ReleaseDrag();
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
                var (region, country) = Pick(motion.Position);
                SetHovered(region, country);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key.Keycode, key.PhysicalKeycode);
                break;
        }
    }

    /// <summary>Клавиши; буквенные — по физическому положению, чтобы работали и в русской раскладке.</summary>
    void HandleKey(Key key, Key physical)
    {
        if (physical is Key.G or Key.L)
        {
            ToggleScreen(physical == Key.G ? "government" : "laws");
            return;
        }
        switch (key)
        {
            case Key.Space:
                _host!.TogglePause();
                return;
            case >= Key.Key1 and <= Key.Key5:
                _host!.SetSpeed((int)(key - Key.Key1) + 1);
                return;
            case Key.Escape:
                if (_government.Visible || _laws.Visible)
                    CloseScreens();
                else
                    SelectRegion(null);
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

    /// <summary>Регион под курсором; если его нет — мелкая страна, нарисованная кружком.</summary>
    (MapRegion? Region, MapCountry? Country) Pick(Vector2 screen)
    {
        var world = _camera.ScreenToWorld(screen);
        var radius = (MapOverlay.MarkerRadius + 3) / _camera.ZoomLevel;
        var marker = _map.FindMarkerAt(world.X, world.Y, radius, _overlay.IsMarker);
        if (marker != null)
            return (null, marker);
        var region = _map.FindRegionAt(world.X, world.Y);
        return (region, region?.Country is { } c ? _map.Country(c) : null);
    }

    void ClickAt(Vector2 screen)
    {
        var (region, country) = Pick(screen);
        if (region != null)
            SelectRegion(region.IsNeutral ? null : region);
        else
            SelectCountry(country);
    }

    void SetHovered(MapRegion? region, MapCountry? country)
    {
        if (region == _hoveredRegion && country == _hoveredCountry)
        {
            UpdateHoverCard();
            return;
        }
        _hoveredRegion = region;
        _hoveredCountry = country;
        _overlay.Hovered = country?.Id;
        _overlay.QueueRedraw();
        UpdateHighlight();
        UpdateHoverCard();
    }

    void UpdateHoverCard()
    {
        if (_hoverCard == null)
            return;
        if (_host == null || (_hoveredRegion == null && _hoveredCountry == null))
        {
            _hoverCard.Visible = false;
            return;
        }
        var snapshot = _host.Snapshot;
        if (_hoveredRegion is { IsNeutral: true } neutral)
        {
            var t = _map.Territory(neutral.Territory!);
            _hoverCard.ShowAt(_mouse, t?.Name ?? neutral.Id, t?.Note ?? "");
            return;
        }
        var region = _hoveredRegion ?? _hoveredCountry!.Regions.MaxBy(r => r.Area)!;
        var countryName = _countryNames[region.Country!];
        var lines = new List<string>();
        var title = countryName;
        if (_hoveredRegion != null && _regionInfo.TryGetValue(region.Id, out var info))
        {
            title = info.Name;
            lines.Add(countryName);
        }
        var controller = snapshot.ControllerOf(region.Id, region.Country!);
        if (controller != region.Country)
            lines.Add("Под контролем: " + _countryNames[controller]);
        var value = _mode.ValueText(region, snapshot);
        if (value.Length > 0)
            lines.Add(value);
        _hoverCard.ShowAt(_mouse, title, string.Join('\n', lines));
    }

    void SelectRegion(MapRegion? region, bool center = false)
    {
        _selectedRegion = region;
        _selectedCountry = region?.Country is { } c ? _map.Country(c) : null;
        ApplySelection(center ? region is null ? null : new Vector2(region.LabelX, region.LabelY) : null);
    }

    void SelectCountry(MapCountry? country, bool center = false)
    {
        _selectedRegion = null;
        _selectedCountry = country;
        ApplySelection(center && country != null ? new Vector2(country.LabelX, country.LabelY) : null);
    }

    void ApplySelection(Vector2? centerOn)
    {
        _overlay.Selected = _selectedCountry?.Id;
        _overlay.QueueRedraw();
        UpdateHighlight();
        if (_selectedCountry == null || _host == null)
        {
            _countryPanel.Visible = false;
            return;
        }
        _countryPanel.ShowCountry(_selectedCountry.Id, _selectedRegion?.Id, _host.Snapshot);
        if (centerOn is { } point)
            _camera.CenterOn(point);
    }

    void SmokeStep(double delta)
    {
        _smokeTime += delta;
        switch (_smokeStep)
        {
            case 0 when _smokeTime > 0.3:
                _host!.SetSpeed(SimulationHost.MaxSpeed);
                SelectCountry(_map.Country("RUS"), center: true);
                _host.Post(w => w.PlayerCountry = "RUS");
                // Захват региона: проверяем, что перекраска и досье переживают смену контроля.
                _smokeOccupied = _map.Country("FRA")!.Regions[0].Id;
                var occupied = _smokeOccupied;
                _host.Post(w => w.TransferControl(occupied, "DEU"));
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
                    SetHovered(region, region.Country is { } c ? _map.Country(c) : null);
                SelectCountry(_map.Country("TUV"));
                SelectRegion(_map.Region(_smokeOccupied!));
                // Правительство: экран открывается, министр меняется, в журнале появляется запись.
                OpenGovernment("RUS");
                _host!.Post(w =>
                {
                    Politeconomy.Sim.Government.GovernmentActions.RequestCandidates(w, "RUS", "health");
                    Politeconomy.Sim.Government.GovernmentActions.Appoint(w, "RUS", "health", 0);
                    Politeconomy.Sim.Laws.LawActions.Propose(w, "RUS", "vat_reduced", "no");
                });
                OpenLaws("RUS");
                _smokeStep++;
                break;
            case 2 when _smokeTime > 3 && (_host!.Snapshot.Date >= _data.Scenario.StartDate.AddMonths(2) || _smokeTime > 20):
                // Экономика должна прожить хотя бы два месяца: показатели пересчитаны моделью и с разбором «почему».
                var snapshot = _host!.Snapshot;
                var inflation = snapshot.Countries["RUS"].Stat("inflation_pct");
                var classes = snapshot.Countries["RUS"].Classes;
                var appointed = snapshot.Journal.Any(e => e.Country == "RUS" && e.Kind == "appointment")
                    && snapshot.Countries["RUS"].Government is { Ministers.Count: 18 }
                    && snapshot.Countries["RUS"].Laws is { } rusLaws && (rusLaws.BillFor("vat_reduced") != null || rusLaws.Current["vat_reduced"] == "no");
                var ok = snapshot.Date >= _data.Scenario.StartDate.AddMonths(2) && snapshot.PlayerCountry == "RUS" && _host.Error == null
                    && snapshot.Occupied.GetValueOrDefault(_smokeOccupied!) == "DEU"
                    && inflation is { Source: StatSources.Model, Factors.Count: > 0 } && double.IsFinite(inflation.Value)
                    && classes is { Count: 10 } && classes.All(c => double.IsFinite(c.Satisfaction)) && appointed;
                if (_host.Error != null)
                    GD.PrintErr(_host.Error.ToString());
                GD.Print($"SMOKE {(ok ? "OK" : "FAIL")}: дата {snapshot.Date:yyyy-MM-dd}, стран {snapshot.Countries.Count}, " +
                         $"регионов {_map.Regions.Count}, занято {snapshot.Occupied.Count}, инфляция в России {inflation?.Value:F2}%, " +
                         $"удовлетворённость {snapshot.Countries["RUS"].Stat("satisfaction")?.Value:F0}, назначение {(appointed ? "есть" : "нет")}, " +
                         $"шаг {_host.LastStepMs:F2} мс");
                GetTree().Quit(ok ? 0 : 1);
                _smokeStep++;
                break;
        }
    }

    /// <summary>
    /// Обход для скриншотов: выставить вид, подождать, пока кадр отрисуется, сохранить PNG.
    /// Время стоит на паузе, поэтому кадры каждый раз одинаковые.
    /// </summary>
    void ScreenshotStep()
    {
        _shotFrames++;
        if (_shotFrames < 45)
            return;
        var views = new (string Name, Action Setup)[]
        {
            ("01_world", () => { SelectRegion(null); SetMode(_data.MapModes.Modes[0]); _camera.FitWorld(instant: true); }),
            ("02_gdp_per_capita", () => SetMode(_data.MapModes.Modes.First(m => m.Id == "gdp_per_capita"))),
            ("03_europe", () => { SetMode(_data.MapModes.Modes[0]); ShowAt("DEU", 4.5f); }),
            ("04_dossier", () => { SelectCountry(_map.Country("RUS")); ShowAt("RUS", 2.2f); }),
            ("05_hover", () =>
            {
                SelectRegion(null);
                ShowAt("FRA", 7f);
                _mouse = _camera.GetViewportRect().Size / 2;
                var (region, country) = Pick(_mouse);
                SetHovered(region, country);
            }),
            ("06_closeup", () => { SetHovered(null, null); ShowAt("NLD", 16f); }),
            ("07_inflation", () => { SetMode(_data.MapModes.Modes.First(m => m.Id == "inflation")); _camera.FitWorld(instant: true); }),
            ("08_population", () =>
            {
                SetMode(_data.MapModes.Modes.First(m => m.Id == "median_age"));
                SelectCountry(_map.Country("JPN"));
                _camera.FitWorld(instant: true);
                _countryPanel.ScrollToSection("Население");
            }),
            ("09_classes", () =>
            {
                SetMode(_data.MapModes.Modes.First(m => m.Id == "living"));
                SelectCountry(_map.Country("BRA"));
                _camera.FitWorld(instant: true);
                _countryPanel.ScrollToSection("Классы");
            }),
            ("10_satisfaction", () => SetMode(_data.MapModes.Modes.First(m => m.Id == "satisfaction"))),
            ("11_government", () =>
            {
                _host!.Post(w => w.PlayerCountry = "BRA");
                SetMode(_data.MapModes.Modes[0]);
                OpenGovernment("BRA");
            }),
            ("12_candidates", () => _government.ChooseReplacement("finance")),
            ("13_parliament_china", () => OpenGovernment("CHN")),
            ("14_laws", () =>
            {
                _host!.Post(w => Politeconomy.Sim.Laws.LawActions.Propose(w, "BRA", "child_benefits", "generous"));
                OpenLaws("BRA");
                _laws.SelectLaw("minimum_wage", "high");
            }),
        };
        if (_shotStep > 0)
        {
            var image = GetViewport().GetTexture().GetImage();
            var path = System.IO.Path.Combine(_shotsDir!, views[_shotStep - 1].Name + ".png");
            image.SavePng(path);
            GD.Print("SCREENSHOT " + path);
        }
        if (_shotStep == views.Length)
        {
            GetTree().Quit();
            _shotsDir = null;
            return;
        }
        views[_shotStep].Setup();
        _shotStep++;
        _shotFrames = 0;
    }

    /// <summary>Открыть или закрыть экран: страны игрока, а если игрок ещё не выбрал страну — выбранной на карте.</summary>
    void ToggleScreen(string screen)
    {
        var open = screen == "government" ? _government.Visible : _laws.Visible;
        if (open)
        {
            CloseScreens();
            return;
        }
        var id = _host?.Snapshot.PlayerCountry ?? _selectedCountry?.Id;
        if (id == null)
        {
            _toasts.Push("Выберите страну на карте или начните игру за неё — тогда откроется её " +
                         (screen == "government" ? "правительство." : "законодательство."));
            _rail.SetActive(null);
            return;
        }
        if (screen == "government")
            OpenGovernment(id);
        else
            OpenLaws(id);
    }

    void OpenGovernment(string countryId)
    {
        if (_host == null)
            return;
        _laws.Visible = false;
        _government.ShowCountry(countryId, _host.Snapshot);
        _rail.SetActive("government");
        SetHovered(null, null);
    }

    void OpenLaws(string countryId)
    {
        if (_host == null)
            return;
        _government.Visible = false;
        _laws.ShowCountry(countryId, _host.Snapshot);
        _rail.SetActive("laws");
        SetHovered(null, null);
    }

    void CloseScreens()
    {
        _government.Visible = false;
        _laws.Visible = false;
        _rail.SetActive(null);
    }

    /// <summary>Показать страну в центре экрана с масштабом «во столько-то раз ближе, чем весь мир».</summary>
    void ShowAt(string countryId, float closer)
    {
        var country = _map.Country(countryId)!;
        _camera.ShowAt(new Vector2(country.LabelX, country.LabelY), closer);
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
