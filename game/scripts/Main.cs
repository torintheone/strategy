using Godot;
using Politeconomy.Game.Map;
using Politeconomy.Game.UI;
using Politeconomy.Sim;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Geo;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;
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
    MinistryScreen _ministries = null!;
    PoliticsScreen _politicsScreen = null!;
    SocietyScreen _society = null!;
    ElectionResultsDialog _resultsDialog = null!;
    CoalitionDialog _coalitionDialog = null!;
    DefeatDialog _defeatDialog = null!;
    ReignSummaryDialog _summaryDialog = null!;
    ConfirmDialog _confirmDialog = null!;
    DebateDialog _debateDialog = null!;
    ProtestDialog _protestDialog = null!;
    readonly HashSet<string> _seenProtests = new();
    string? _seenCampaign;
    string? _seenDebate;
    bool _dialogWasOpen;
    // Какие выборы, переговоры, поражения и итоги игрок уже видел (окна показываются один раз).
    readonly HashSet<string> _seenElections = new();
    string? _seenFor;
    string? _seenTalks;
    Politeconomy.Sim.Politics.Defeat? _seenDefeat;
    Politeconomy.Sim.Politics.ReignSummary? _seenSummary;
    bool _dialogsPrimed;
    LawsScreen _laws = null!;
    BudgetScreen _budget = null!;
    bool _dossierBeforeScreen;
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
        _countryPanel.PlayAs += id => _host!.Post(w => Politeconomy.Sim.Politics.PoliticsActions.PlayAs(w, id));
        _countryPanel.RegionChosen += id => SelectRegion(_map.Region(id), center: true);

        _countryPanel.GovernmentRequested += id => OpenGovernment(id);
        _countryPanel.PoliticsRequested += id => OpenPolitics(id);

        _rail = new ScreenRail();
        _uiRoot.AddChild(_rail);
        _rail.Setup([("government", "government", "Правительство и парламент (G)"), ("ministries", "ministries", "Ведомства и доклады министров (M)"),
            ("politics", "politics", "Партии и опросы (P)"), ("society", "society", "Общество и группы интересов (O)"), ("laws", "laws", "Законы (L)"),
            ("budget", "budget", "Бюджет и налоги (B)")]);
        _rail.ScreenChosen += ToggleScreen;

        _government = new GovernmentScreen { Visible = false };
        _uiRoot.AddChild(_government);
        _government.Setup(_data);
        _government.Closed += CloseScreens;
        _government.CandidatesRequested += (country, post) =>
            _host!.Post(w => Politeconomy.Sim.Government.GovernmentActions.RequestCandidates(w, country, post));
        _government.Appointed += (country, post, index) =>
            _host!.Post(w => Politeconomy.Sim.Government.GovernmentActions.Appoint(w, country, post, index));

        _ministries = new MinistryScreen { Visible = false };
        _uiRoot.AddChild(_ministries);
        _ministries.Setup(_data);
        _ministries.Closed += CloseScreens;
        _ministries.ReplaceRequested += (country, post) =>
        {
            OpenGovernment(country);
            _government.ChooseReplacement(post);
        };
        _ministries.LawRequested += (country, law) =>
        {
            OpenLaws(country);
            _laws.SelectLaw(law);
        };
        _ministries.BudgetRequested += OpenBudget;

        _politicsScreen = new PoliticsScreen { Visible = false };
        _uiRoot.AddChild(_politicsScreen);
        _politicsScreen.Setup(_data);
        _politicsScreen.Closed += CloseScreens;
        _politicsScreen.ResultsRequested += id => ShowLastElection(id);
        _politicsScreen.TalksRequested += id =>
        {
            if (_host?.Snapshot.Countries.GetValueOrDefault(id) is { } c)
                _coalitionDialog.Show(id, CountryName(id), c);
        };
        var campaign = _politicsScreen.Campaign;
        campaign.TripRequested += (id, k) => _host!.Post(w => Politeconomy.Sim.Politics.PoliticsActions.CampaignTrip(w, id, k));
        campaign.MediaRequested += id => _host!.Post(w => Politeconomy.Sim.Politics.PoliticsActions.CampaignMedia(w, id));
        campaign.PledgeRequested += (id, law, variant) => _host!.Post(w => Politeconomy.Sim.Politics.PoliticsActions.Pledge(w, id, law, variant));
        campaign.FraudChosen += (id, level) => _host!.Post(w => Politeconomy.Sim.Politics.PoliticsActions.SetFraud(w, id, level));
        campaign.DebateRequested += ShowDebate;
        _politicsScreen.EarlyElectionRequested += id => _confirmDialog.Show("Распустить парламент?",
            "Досрочные выборы пройдут через два месяца. Если ваша партия проиграет, власть перейдёт к другим, и вам придётся решать — " +
            "продолжать за новое правительство или закончить партию.", "Распустить",
            () => _host!.Post(w => Politeconomy.Sim.Politics.PoliticsActions.CallEarlyElection(w, id)));

        _society = new SocietyScreen { Visible = false };
        _uiRoot.AddChild(_society);
        _society.Setup(_data);
        _society.Closed += CloseScreens;
        _society.LawRequested += (country, law, variant) =>
        {
            OpenLaws(country);
            _laws.SelectLaw(law, variant);
        };
        _society.BudgetRequested += _ => OpenBudget();
        _society.ProtestRequested += ShowProtest;
        _society.ArrestRequested += id =>
        {
            var p = _host?.Snapshot.Countries.GetValueOrDefault(id)?.Politics;
            var target = p?.Party(p.Security?.ArrestTarget);
            _confirmDialog.Show("Арестовать лидеров оппозиции?",
                $"Будут задержаны лидер и активисты партии «{target?.Name}». Партия ослабнет и потеряет часть сторонников. Цена: свобод станет меньше, " +
                "легитимность упадёт, радикализм вырастет, сторонники партии выйдут на улицы, интеллигенция и профсоюзы будут недовольны, " +
                "а при свободной прессе случится скандал. Повторить можно не раньше чем через год.",
                "Арестовать", () => _host!.Post(w => Politeconomy.Sim.Politics.PoliticsActions.ArrestOpposition(w, id)));
        };

        _laws = new LawsScreen { Visible = false };
        _uiRoot.AddChild(_laws);
        _laws.Setup(_data);
        _laws.Closed += CloseScreens;
        _laws.Proposed += (country, law, variant) =>
            _host!.Post(w => Politeconomy.Sim.Laws.LawActions.Propose(w, country, law, variant));
        _laws.Withdrawn += (country, law) =>
            _host!.Post(w => Politeconomy.Sim.Laws.LawActions.Withdraw(w, country, law));

        _budget = new BudgetScreen { Visible = false };
        _uiRoot.AddChild(_budget);
        _budget.Setup(_data);
        _budget.Closed += CloseScreens;
        _budget.Edited += (country, spending, taxes) =>
            _host!.Post(w => Politeconomy.Sim.Budget.BudgetActions.Edit(w, country, spending, taxes));
        _budget.AmendmentSubmitted += country => _host!.Post(w => Politeconomy.Sim.Budget.BudgetActions.SubmitAmendment(w, country));
        _budget.AmendmentDiscarded += country => _host!.Post(w => Politeconomy.Sim.Budget.BudgetActions.DiscardAmendment(w, country));
        _budget.RateChosen += (country, rate) => _host!.Post(w => Politeconomy.Sim.Budget.BudgetActions.SetRate(w, country, rate));

        _resultsDialog = new ElectionResultsDialog();
        _uiRoot.AddChild(_resultsDialog);
        _resultsDialog.Setup();
        _resultsDialog.MapRequested += ShowElectionMap;
        _coalitionDialog = new CoalitionDialog();
        _uiRoot.AddChild(_coalitionDialog);
        _coalitionDialog.Setup(_data);
        _coalitionDialog.Formed += (country, partners) =>
            _host!.Post(w => Politeconomy.Sim.Politics.PoliticsActions.FormCoalition(w, country, partners));
        _defeatDialog = new DefeatDialog();
        _uiRoot.AddChild(_defeatDialog);
        _defeatDialog.Setup();
        _defeatDialog.ContinueChosen += () => _host!.Post(Politeconomy.Sim.Politics.PoliticsActions.ContinueAfterDefeat);
        _defeatDialog.EndChosen += () =>
        {
            CloseScreens();
            _host!.Post(Politeconomy.Sim.Politics.PoliticsActions.EndReign);
        };
        _summaryDialog = new ReignSummaryDialog();
        _uiRoot.AddChild(_summaryDialog);
        _summaryDialog.Setup(_data);
        _confirmDialog = new ConfirmDialog();
        _uiRoot.AddChild(_confirmDialog);
        _confirmDialog.Setup();
        _debateDialog = new DebateDialog();
        _uiRoot.AddChild(_debateDialog);
        _debateDialog.Setup();
        _debateDialog.Answered += (id, answer) => _host!.Post(w => Politeconomy.Sim.Politics.PoliticsActions.AnswerDebate(w, id, answer));
        _protestDialog = new ProtestDialog();
        _uiRoot.AddChild(_protestDialog);
        _protestDialog.Setup(_data);
        _protestDialog.Answered += (id, protest, response) =>
            _host!.Post(w => Politeconomy.Sim.Politics.PoliticsActions.RespondToProtest(w, id, protest, response));

        _toasts = new Toasts();
        _uiRoot.AddChild(_toasts);
        _toasts.Setup();

        _hoverCard = new HoverCard();
        _uiRoot.AddChild(_hoverCard);
        _hoverCard.Setup();

        // Когда курсор уходит на панель, карта не должна подсвечивать регион под ней.
        foreach (var panel in new Control[] { _topBar, _modePanel, _countryPanel, _rail, _government, _ministries, _politicsScreen, _laws, _budget })
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
        // Окно закрыли — следующее (например, дебаты после начала кампании) показываем сразу: на паузе нового снимка не будет.
        if (_dialogWasOpen && !DialogOpen)
            CheckDialogs(snapshot);
        _dialogWasOpen = DialogOpen;
        _dialogWasOpen = DialogOpen;
        if (snapshot.Version == _shownVersion)
            return;
        _toasts.Feed(snapshot.Journal, snapshot.PlayerCountry, silent: _shownVersion < 0);
        _shownVersion = snapshot.Version;
        _topBar.Show(snapshot);
        if (_government.Visible)
            _government.Refresh(snapshot);
        if (_ministries.Visible)
            _ministries.Refresh(snapshot);
        if (_politicsScreen.Visible)
            _politicsScreen.Refresh(snapshot);
        if (_society.Visible)
            _society.Refresh(snapshot);
        if (_laws.Visible)
            _laws.Refresh(snapshot);
        if (_budget.Visible)
            _budget.Refresh(snapshot);
        Recolor(snapshot);
        CheckDialogs(snapshot);
        _dialogWasOpen = DialogOpen;
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
        // Пока открыто окно, клавиши его не обходят; Esc закрывает его (кроме окна поражения — там нужно решить).
        if (DialogOpen)
        {
            if (key == Key.Escape && !_defeatDialog.Visible)
                CloseDialogs();
            return;
        }
        if (physical is Key.G or Key.M or Key.P or Key.O or Key.L or Key.B)
        {
            ToggleScreen(physical switch { Key.G => "government", Key.M => "ministries", Key.P => "politics", Key.O => "society", Key.L => "laws", _ => "budget" });
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
                if (ScreenOpen)
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
                _host.Post(w => Politeconomy.Sim.Politics.PoliticsActions.PlayAs(w, "RUS"));
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
                    var e = w.Economy!["RUS"];
                    var spending = e.SpendingPlan.ToArray();
                    spending[0] *= 1.05;
                    Politeconomy.Sim.Budget.BudgetActions.Edit(w, "RUS", spending, e.TaxPolicy.ToArray());
                    Politeconomy.Sim.Budget.BudgetActions.SubmitAmendment(w, "RUS");
                });
                OpenMinistries("RUS");
                foreach (var post in _data.Government.Posts)
                    _ministries.SelectPost(post.Id);
                OpenPolitics("DEU");
                OpenPolitics("SAU");
                OpenPolitics("RUS");
                OpenSociety("RUS");
                OpenSociety("BRA");
                OpenSociety("SEN");
                OpenPolitics("SAU");
                // Выборы: в стране игрока (окно итогов покажется само) и президентские в Бразилии; окна поражения и итогов.
                _host!.Post(w =>
                {
                    Politeconomy.Sim.Politics.Elections.HoldPresident(w, w["BRA"]);
                    Politeconomy.Sim.Politics.Elections.HoldParliament(w, w["RUS"]);
                    // Кампания: выборы в России через два месяца — штаб и дебаты появятся в первый же месяц.
                    w.Politics!["RUS"].NextElection = w.Date.AddMonths(2);
                    // Протест в России: митинги специалистов; ответ — разгон.
                    var protest = Politeconomy.Sim.Politics.ProtestModel.Start(w, w["RUS"], w.Politics!["RUS"], Politeconomy.Sim.Society.ClassIds.Professionals, "mood", 1);
                    Politeconomy.Sim.Politics.PoliticsActions.RespondToProtest(w, "RUS", protest.Id, "disperse");
                    // Смена власти без выборов в чужих странах: переворот, дворцовый переворот, революция.
                    Politeconomy.Sim.Politics.PowerModel.MilitaryCoup(w, w["SEN"], w.Politics!["SEN"]);
                    Politeconomy.Sim.Politics.PowerModel.PalaceCoup(w, w["PRK"], w.Politics!["PRK"]);
                    Politeconomy.Sim.Politics.PowerModel.Revolution(w, w["SAU"], w.Politics!["SAU"], Politeconomy.Sim.Society.ClassIds.Professionals);
                });
                _defeatDialog.Show(new Politeconomy.Sim.Politics.Defeat(_data.Scenario.StartDate, "RUS", "А", "Б", "В"), "Россия");
                _summaryDialog.Show(new Politeconomy.Sim.Politics.ReignSummary("RUS", _data.Scenario.StartDate, _data.Scenario.StartDate.AddYears(3),
                    new Dictionary<string, double> { ["gdp_per_capita_usd"] = 10000 }, new Dictionary<string, double> { ["gdp_per_capita_usd"] = 12000 }, 1, 0, 3),
                    "Россия");
                CloseDialogs();
                OpenLaws("RUS");
                OpenBudget();
                _smokeStep++;
                break;
            case 2 when _smokeTime > 3 && (_host!.Snapshot.Date >= _data.Scenario.StartDate.AddMonths(2) || _smokeTime > 20):
                // Экономика должна прожить хотя бы два месяца: показатели пересчитаны моделью и с разбором «почему».
                var snapshot = _host!.Snapshot;
                var inflation = snapshot.Countries["RUS"].Stat("inflation_pct");
                var classes = snapshot.Countries["RUS"].Classes;
                var appointed = snapshot.Journal.Any(e => e.Country == "RUS" && e.Kind == "appointment")
                    && snapshot.Countries["RUS"].Government is { Ministers.Count: 18 }
                    && snapshot.Countries["RUS"].Laws is { } rusLaws && (rusLaws.BillFor("vat_reduced") != null || rusLaws.Current["vat_reduced"] == "no")
                    && snapshot.Journal.Any(e => e.Country == "RUS" && e.Kind == "budget")
                    && snapshot.Countries["RUS"].History?.Series("gdp_growth_pct").Count >= 5
                    && snapshot.Countries["DEU"].Politics is { Parties.Count: >= 3, Polls.Count: >= 2 }
                    && snapshot.Countries["RUS"].Politics?.Elections?.Count >= 2 && snapshot.Countries["BRA"].Politics?.LastElection?.Kind == "president"
                    && snapshot.Countries["DEU"].Stat("ruling_poll_pct") is { } ruling && double.IsFinite(ruling.Value)
                    && snapshot.Journal.Any(e => e.Country == "RUS" && e.Kind == "campaign")
                    && snapshot.Journal.Any(e => e.Country == "RUS" && e.Kind == "protest")
                    && snapshot.Countries["SEN"].System == PoliticalSystem.MilitaryRegime && snapshot.Countries["SAU"].Politics is { Parties.Count: > 0 };
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
                _host!.Post(w => Politeconomy.Sim.Politics.PoliticsActions.PlayAs(w, "BRA"));
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
            ("15_budget", () =>
            {
                _host!.Post(w =>
                {
                    var e = w.Economy!["BRA"];
                    var spending = e.SpendingPlan.ToArray();
                    spending[0] *= 1.2;
                    var taxes = e.TaxPolicy.ToArray();
                    taxes[0] = 1.15;
                    Politeconomy.Sim.Budget.BudgetActions.Edit(w, "BRA", spending, taxes);
                });
                OpenBudget();
            }),
            ("16_ministry_health", () =>
            {
                OpenMinistries("BRA");
                _ministries.SelectPost("health");
            }),
            ("17_ministry_finance", () => _ministries.SelectPost("finance")),
            ("18_politics", () => OpenPolitics("BRA")),
            ("19_politics_germany", () => OpenPolitics("DEU")),
            ("20_map_parties", () =>
            {
                CloseScreens();
                SelectRegion(null);
                SetMode(_data.MapModes.Modes.First(m => m.Id == "parties"));
                _camera.FitWorld(instant: true);
            }),
            // Выборы в стране игрока: окно итогов открывается само, когда появляется новый итог.
            ("21_election_president", () => _host!.Post(w => Politeconomy.Sim.Politics.Elections.HoldPresident(w, w["BRA"]))),
            ("22_election_parliament", () =>
            {
                CloseDialogs();
                _host!.Post(w => Politeconomy.Sim.Politics.Elections.HoldParliament(w, w["BRA"]));
            }),
            ("23_election_map", () =>
            {
                CloseDialogs();
                _seenDefeat = _host!.Snapshot.Defeat;
                _host.Post(Politeconomy.Sim.Politics.PoliticsActions.ContinueAfterDefeat);
                ShowElectionMap();
            }),
            ("24_coalition", () =>
            {
                CloseDialogs();
                _host!.Post(w =>
                {
                    Politeconomy.Sim.Politics.PoliticsActions.PlayAs(w, "NLD");
                    var p = w.Politics!["NLD"];
                    var seats = p.Parties.Select(x => w.Government!["NLD"].Parliament.Factions.FirstOrDefault(f => f.Party == x.Id)?.Seats ?? 0).ToArray();
                    Politeconomy.Sim.Politics.CoalitionTalksModel.Start(w, w["NLD"], Politeconomy.Sim.Politics.PoliticsModel.HeadParty(w, "NLD")!, seats);
                });
            }),
            ("25_defeat", () =>
            {
                CloseDialogs();
                _host!.Post(w =>
                {
                    var p = w.Politics!["NLD"];
                    var rival = p.Parties.Where(x => !x.Ruling && x.Leader != null).OrderByDescending(x => x.Poll).First();
                    w.Defeat = new Politeconomy.Sim.Politics.Defeat(w.Date, "NLD", w.Government!["NLD"].Leader.Name, rival.Leader!.Name, rival.Name);
                });
            }),
            ("26_summary", () =>
            {
                CloseDialogs();
                _host!.Post(Politeconomy.Sim.Politics.PoliticsActions.EndReign);
            }),
            // Кампания в Бразилии: выборы переносим на месяц вперёд — сразу и штаб, и дебаты.
            ("27_campaign_start", () =>
            {
                CloseDialogs();
                OpenPolitics("BRA");
                _host!.Post(w =>
                {
                    Politeconomy.Sim.Politics.PoliticsActions.PlayAs(w, "BRA");
                    var p = w.Politics!["BRA"];
                    p.NextElection = p.NextPresident = w.Date.AddMonths(1);
                    Politeconomy.Sim.Politics.CampaignModel.Update(w, w["BRA"], p);
                    Politeconomy.Sim.Politics.PoliticsActions.CampaignTrip(w, "BRA", 1);
                    if (Politeconomy.Sim.Politics.CampaignModel.Estimate(w, w["BRA"]) is { Options.Count: > 0 } o)
                        Politeconomy.Sim.Politics.PoliticsActions.Pledge(w, "BRA", o.Options[0].Law, o.Options[0].Variant);
                    Politeconomy.Sim.Politics.PoliticsActions.SetFraud(w, "BRA", 1);
                });
            }),
            ("28_debate", CloseDialogs),
            ("29_campaign", () =>
            {
                CloseDialogs();
                _politicsScreen.FocusCampaign();
            }),
            ("30_society", () =>
            {
                CloseDialogs();
                OpenSociety("BRA");
            }),
            ("31_society_saudi", () => OpenSociety("SAU")),
            // Протест в Бразилии: окно ответа открывается само.
            ("32_protest", () =>
            {
                OpenSociety("BRA");
                _host!.Post(w =>
                {
                    var p = w.Politics!["BRA"];
                    Politeconomy.Sim.Politics.ProtestModel.Start(w, w["BRA"], p, Politeconomy.Sim.Society.ClassIds.Skilled, "mood", 0);
                    var law = w.Data.Law("retirement_age");
                    Politeconomy.Sim.Politics.ProtestModel.Start(w, w["BRA"], p, Politeconomy.Sim.Society.ClassIds.Pensioners, "law", 2, law.Id, w.Laws!["BRA"].Current[law.Id]);
                });
            }),
            ("33_society_protests", () =>
            {
                CloseDialogs();
                OpenSociety("BRA");
            }),
            ("34_map_unrest", () =>
            {
                CloseScreens();
                _host!.Post(w =>
                {
                    foreach (var id in new[] { "FRA", "IRN", "VEN", "NGA", "ARG" })
                        Politeconomy.Sim.Politics.ProtestModel.Start(w, w[id], w.Politics![id], Politeconomy.Sim.Society.ClassIds.Unskilled, "mood", id == "VEN" ? 3 : 2);
                });
                SetMode(_data.MapModes.Modes.First(m => m.Id == "unrest"));
                _camera.FitWorld(instant: true);
            }),
            // Военный переворот в стране игрока: окно потери власти открывается само.
            ("35_coup", () =>
            {
                SetMode(_data.MapModes.Modes[0]);
                _host!.Post(w => Politeconomy.Sim.Politics.PowerModel.MilitaryCoup(w, w["BRA"], w.Politics!["BRA"]));
            }),
            ("36_junta", () =>
            {
                CloseDialogs();
                _seenDefeat = _host!.Snapshot.Defeat;
                _host.Post(Politeconomy.Sim.Politics.PoliticsActions.ContinueAfterDefeat);
                OpenSociety("BRA");
            }),
            ("37_ministry_education", () =>
            {
                OpenMinistries("DEU");
                _ministries.SelectPost("education");
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
    bool ScreenOpen => _government.Visible || _ministries.Visible || _politicsScreen.Visible || _society.Visible || _laws.Visible || _budget.Visible;

    void ToggleScreen(string screen)
    {
        var open = screen switch
        {
            "government" => _government.Visible,
            "ministries" => _ministries.Visible,
            "politics" => _politicsScreen.Visible,
            "society" => _society.Visible,
            "laws" => _laws.Visible,
            _ => _budget.Visible,
        };
        if (open)
        {
            CloseScreens();
            return;
        }
        if (screen == "budget")
        {
            OpenBudget();
            return;
        }
        var id = _host?.Snapshot.PlayerCountry ?? _selectedCountry?.Id;
        if (id == null)
        {
            _toasts.Push("Выберите страну на карте или начните игру за неё — тогда откроется её " +
                         (screen switch { "government" => "правительство.", "ministries" => "ведомства.", "politics" => "партии.", "society" => "общество.", _ => "законодательство." }));
            _rail.SetActive(null);
            return;
        }
        if (screen == "government")
            OpenGovernment(id);
        else if (screen == "ministries")
            OpenMinistries(id);
        else if (screen == "politics")
            OpenPolitics(id);
        else if (screen == "society")
            OpenSociety(id);
        else
            OpenLaws(id);
    }

    /// <summary>Перед открытием экрана прячем досье и легенду карты: экран закрывает их, а сквозь стекло они бы просвечивали.</summary>
    void HideUnderScreens()
    {
        if (!ScreenOpen)
            _dossierBeforeScreen = _countryPanel.Visible;
        _countryPanel.Visible = false;
        _modePanel.Visible = false;
        _government.Visible = false;
        _ministries.Visible = false;
        _politicsScreen.Visible = false;
        _society.Visible = false;
        _laws.Visible = false;
        _budget.Visible = false;
    }

    void OpenBudget()
    {
        if (_host == null)
            return;
        HideUnderScreens();
        _budget.Open(_host.Snapshot);
        _rail.SetActive("budget");
        SetHovered(null, null);
    }

    void OpenGovernment(string countryId)
    {
        if (_host == null)
            return;
        HideUnderScreens();
        _government.ShowCountry(countryId, _host.Snapshot);
        _rail.SetActive("government");
        SetHovered(null, null);
    }

    void OpenMinistries(string countryId)
    {
        if (_host == null)
            return;
        HideUnderScreens();
        _ministries.ShowCountry(countryId, _host.Snapshot);
        _rail.SetActive("ministries");
        SetHovered(null, null);
    }

    void OpenPolitics(string countryId)
    {
        if (_host == null)
            return;
        HideUnderScreens();
        _politicsScreen.ShowCountry(countryId, _host.Snapshot);
        _rail.SetActive("politics");
        SetHovered(null, null);
    }

    void OpenSociety(string countryId)
    {
        if (_host == null)
            return;
        HideUnderScreens();
        _society.ShowCountry(countryId, _host.Snapshot);
        _rail.SetActive("society");
        SetHovered(null, null);
    }

    void OpenLaws(string countryId)
    {
        if (_host == null)
            return;
        HideUnderScreens();
        _laws.ShowCountry(countryId, _host.Snapshot);
        _rail.SetActive("laws");
        SetHovered(null, null);
    }

    bool DialogOpen => _resultsDialog.Visible || _coalitionDialog.Visible || _defeatDialog.Visible || _summaryDialog.Visible || _confirmDialog.Visible
                       || _debateDialog.Visible || _protestDialog.Visible;

    string CountryName(string id) => _data.Countries.Countries.First(c => c.Id == id).Name;

    static string ElectionKey(string country, ElectionSnapshot e) => $"{country}/{e.Date}/{e.Kind}";

    /// <summary>
    /// Запомнить прошлые выборы страны игрока как просмотренные (на старте и при смене страны); итоги правления —
    /// только на старте, иначе окно итогов не покажется после «Закончить партию» (тогда страна игрока тоже меняется).
    /// </summary>
    void PrimeDialogs(WorldSnapshot snapshot)
    {
        _seenFor = snapshot.PlayerCountry;
        if (_seenFor != null && snapshot.Countries[_seenFor].Politics?.Elections is { } elections)
            foreach (var e in elections)
                _seenElections.Add(ElectionKey(_seenFor, e));
        if (!_dialogsPrimed)
            _seenSummary = snapshot.LastReign;
        _dialogsPrimed = true;
    }

    /// <summary>
    /// Окна о стране игрока по одному: новые итоги выборов, поражение, переговоры о коалиции, начало кампании, дебаты,
    /// протест (новый или разгоревшийся, с митингов и выше), итоги правления. Пока окно открыто, время стоит.
    /// </summary>
    void CheckDialogs(WorldSnapshot snapshot)
    {
        if (!_dialogsPrimed || snapshot.PlayerCountry != _seenFor)
            PrimeDialogs(snapshot);
        if (DialogOpen)
            return;
        var player = snapshot.PlayerCountry;
        var country = player != null ? snapshot.Countries[player] : null;
        if (country?.Politics?.Elections?.FirstOrDefault(e => !_seenElections.Contains(ElectionKey(player!, e))) is { } election)
        {
            _seenElections.Add(ElectionKey(player!, election));
            _resultsDialog.Show(CountryName(player!), country, election);
            PauseForDialog();
        }
        else if (snapshot.Defeat is { } defeat && defeat != _seenDefeat)
        {
            _seenDefeat = defeat;
            _defeatDialog.Show(defeat, CountryName(defeat.Country));
            PauseForDialog();
        }
        else if (country?.Politics?.Talks is { } talks && $"{player}/{talks.Date}" != _seenTalks)
        {
            _seenTalks = $"{player}/{talks.Date}";
            _coalitionDialog.Show(player!, CountryName(player!), country);
            PauseForDialog();
        }
        else if (country?.Politics?.Campaign is { } campaign && $"{player}/{campaign.Election}" != _seenCampaign)
        {
            _seenCampaign = $"{player}/{campaign.Election}";
            var id = player!;
            _confirmDialog.Show("Началась предвыборная кампания",
                $"Выборы {Fmt.Date(campaign.Election)}. Каждый месяц штаб получает {Fmt.Plural((long)campaign.PointsPerMonth, "очко", "очка", "очков")}: " +
                "на поездки к классам, агитацию в СМИ и обещания избирателям. За месяц до выборов — дебаты лидеров. " +
                "Можно и подправить подсчёт голосов, но фальсификации могут раскрыть.",
                "Открыть штаб", () =>
                {
                    OpenPolitics(id);
                    _politicsScreen.FocusCampaign();
                }, "Позже");
            PauseForDialog();
        }
        else if (country?.Politics?.Campaign?.Debate is { Answer: null } debate && $"{player}/{debate.Date}" != _seenDebate)
        {
            _seenDebate = $"{player}/{debate.Date}";
            ShowDebate(player!);
            PauseForDialog();
        }
        else if (country?.Politics?.Protests?.FirstOrDefault(x => x is { Stage: >= 1, Answered: false } && !_seenProtests.Contains($"{player}/{x.Id}/{x.Stage}")) is { } protest)
        {
            _seenProtests.Add($"{player}/{protest.Id}/{protest.Stage}");
            ShowProtest(player!, protest.Id);
            PauseForDialog();
        }
        else if (snapshot.LastReign is { } summary && !ReferenceEquals(summary, _seenSummary))
        {
            _seenSummary = summary;
            _summaryDialog.Show(summary, CountryName(summary.Country));
        }
    }

    void PauseForDialog()
    {
        if (!_smoke)
            _host?.SetPaused(true);
    }

    void CloseDialogs()
    {
        _resultsDialog.Visible = false;
        _coalitionDialog.Visible = false;
        _defeatDialog.Visible = false;
        _summaryDialog.Visible = false;
        _confirmDialog.Visible = false;
        _debateDialog.Visible = false;
        _protestDialog.Visible = false;
    }

    void ShowProtest(string countryId, int protestId)
    {
        if (_host?.Snapshot.Countries.GetValueOrDefault(countryId)?.Politics is { } p && p.Protests?.FirstOrDefault(x => x.Id == protestId) is { Answered: false } protest)
            _protestDialog.Show(countryId, CountryName(countryId), protest, p.Security);
    }

    void ShowDebate(string countryId)
    {
        if (_host?.Snapshot.Countries.GetValueOrDefault(countryId) is { Politics.Campaign.Debate: { Answer: null } debate } country)
            _debateDialog.Show(countryId, CountryName(countryId), country.Government?.Leader, debate);
    }

    void ShowLastElection(string countryId)
    {
        if (_host?.Snapshot.Countries.GetValueOrDefault(countryId) is { Politics.LastElection: { } last } country)
            _resultsDialog.Show(CountryName(countryId), country, last);
    }

    void ShowElectionMap()
    {
        CloseScreens();
        SetMode(_data.MapModes.Modes.First(m => m.Id == "elections"));
        if (_host?.Snapshot.PlayerCountry is { } id)
            ShowAt(id, 2.5f);
    }

    void CloseScreens()
    {
        if (ScreenOpen)
        {
            _countryPanel.Visible = _dossierBeforeScreen && _selectedCountry != null;
            _modePanel.Visible = true;
        }
        _government.Visible = false;
        _ministries.Visible = false;
        _politicsScreen.Visible = false;
        _society.Visible = false;
        _laws.Visible = false;
        _budget.Visible = false;
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
