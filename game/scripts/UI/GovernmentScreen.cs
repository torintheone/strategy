using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Экран «Правительство»: глава исполнительной власти, парламент полукругом и 18 министров. В своей стране игрок
/// может сменить министра: игра предлагает трёх кандидатов, из них выбирается один.
/// </summary>
public partial class GovernmentScreen : GlassPanel
{
    GameData _data = null!;
    Dictionary<string, PostRecord> _posts = null!;
    Dictionary<string, TraitRecord> _traits = null!;
    Label _title = null!;
    Label _subtitle = null!;
    Portrait _leaderPortrait = null!;
    Label _leaderRole = null!;
    Label _leaderName = null!;
    Label _leaderInfo = null!;
    Meter _leaderCompetence = null!;
    Meter _leaderPopularity = null!;
    Control _leaderCard = null!;
    Label _parliamentName = null!;
    Label _parliamentNote = null!;
    ParliamentChart _chart = null!;
    Label _parliamentLegend = null!;
    readonly Dictionary<string, MinisterCard> _cards = new();
    Control _picker = null!;
    Label _pickerTitle = null!;
    HBoxContainer _pickerRow = null!;
    string? _countryId;
    string? _pickingPost;
    GovernmentSnapshot? _shown;
    bool _shownPlayer;
    IReadOnlyList<CharacterSnapshot>? _shownCandidates;

    public event Action? Closed;
    public event Action<string, string>? CandidatesRequested;
    public event Action<string, string, int>? Appointed;

    public string? CountryId => _countryId;

    public void Setup(GameData data)
    {
        _data = data;
        _posts = data.Government.Posts.ToDictionary(p => p.Id);
        _traits = data.Government.Traits.ToDictionary(t => t.Id);
        SetAnchorsPreset(LayoutPreset.FullRect);
        OffsetLeft = 76;
        OffsetRight = -12;
        OffsetTop = 78;
        OffsetBottom = -12;
        MouseFilter = MouseFilterEnum.Stop;
        InitGlass(radius: 16, padH: 22, padV: 18, tint: 0.9f);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        Body.AddChild(column);

        var header = new HBoxContainer();
        column.AddChild(header);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        header.AddChild(titles);
        _title = UiTheme.Text("", 26, UiTheme.SerifBold);
        titles.AddChild(_title);
        _subtitle = UiTheme.Text("", 13, null, UiTheme.TextMuted);
        titles.AddChild(_subtitle);
        var close = new Button { Text = "×", Flat = true, TooltipText = "Закрыть (Esc)", FocusMode = FocusModeEnum.None };
        close.AddThemeFontSizeOverride("font_size", 22);
        close.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        close.Pressed += () => Closed?.Invoke();
        header.AddChild(close);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 16);
        column.AddChild(top);
        top.AddChild(BuildLeader());
        top.AddChild(BuildParliament());

        column.AddChild(UiTheme.Text("МИНИСТРЫ И РУКОВОДИТЕЛИ ВЕДОМСТВ", 12, UiTheme.MapCountryFont, UiTheme.Accent));
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        scroll.AddChild(grid);
        foreach (var post in data.Government.Posts)
        {
            var card = new MinisterCard();
            card.Setup(post);
            card.ReplaceRequested += () => ChooseReplacement(post.Id);
            grid.AddChild(card);
            _cards[post.Id] = card;
        }

        _picker = BuildPicker();
        AddChild(_picker);
    }

    Control BuildLeader()
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.04f), UiTheme.PanelBorder, 12, 14, 12));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        card.AddChild(row);
        _leaderPortrait = new Portrait { CustomMinimumSize = new Vector2(104, 136), SizeFlagsVertical = SizeFlags.ShrinkBegin };
        row.AddChild(_leaderPortrait);
        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", 4);
        row.AddChild(info);
        _leaderRole = UiTheme.Text("", 12, UiTheme.MapCountryFont, UiTheme.Accent);
        info.AddChild(_leaderRole);
        _leaderName = UiTheme.Text("", 22, UiTheme.SemiBold);
        info.AddChild(_leaderName);
        _leaderInfo = UiTheme.Text("", 13, null, UiTheme.TextSecondary);
        _leaderInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        info.AddChild(_leaderInfo);
        _leaderCompetence = new Meter { CustomMinimumSize = new Vector2(320, 16), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _leaderPopularity = new Meter { CustomMinimumSize = new Vector2(320, 16), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        info.AddChild(_leaderCompetence);
        info.AddChild(_leaderPopularity);
        _leaderCard = card;
        return card;
    }

    Control BuildParliament()
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(380, 0) };
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.04f), UiTheme.PanelBorder, 12, 14, 12));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        card.AddChild(column);
        _parliamentName = UiTheme.Text("", 12, UiTheme.MapCountryFont, UiTheme.Accent);
        column.AddChild(_parliamentName);
        _chart = new ParliamentChart { CustomMinimumSize = new Vector2(340, 120) };
        column.AddChild(_chart);
        _parliamentLegend = UiTheme.Text("", 13, UiTheme.Numbers, UiTheme.TextSecondary);
        _parliamentLegend.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_parliamentLegend);
        _parliamentNote = UiTheme.Text("", 12, null, UiTheme.TextMuted);
        _parliamentNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _parliamentNote.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_parliamentNote);
        return card;
    }

    Control BuildPicker()
    {
        // Выбор замены: затемнение поверх экрана и панель с тремя кандидатами.
        var dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.04f, 0.72f), Visible = false, MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        dim.AddChild(center);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.PanelRaised, UiTheme.PanelBorder, 14, 20, 18));
        center.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);
        _pickerTitle = UiTheme.Text("", 18, UiTheme.SemiBold);
        column.AddChild(_pickerTitle);
        column.AddChild(UiTheme.Text("Кого назначить? Прежний министр уйдёт в отставку, новый будет благодарен за назначение.", 13, null, UiTheme.TextSecondary));
        _pickerRow = new HBoxContainer();
        _pickerRow.AddThemeConstantOverride("separation", 12);
        column.AddChild(_pickerRow);
        var cancel = new Button { Text = "Оставить прежнего министра", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
        cancel.Pressed += () =>
        {
            _pickingPost = null;
            _picker.Visible = false;
        };
        column.AddChild(cancel);
        return dim;
    }

    public void ShowCountry(string countryId, WorldSnapshot snapshot)
    {
        if (_countryId != countryId)
        {
            _pickingPost = null;
            _picker.Visible = false;
            _shown = null;
        }
        _countryId = countryId;
        var appearing = !Visible;
        Visible = true;
        Refresh(snapshot);
        if (appearing)
            AnimateIn(new Vector2(0, 0.1f), 0.2f, 0.985f);
    }

    public void Refresh(WorldSnapshot snapshot)
    {
        if (_countryId == null || !snapshot.Countries.TryGetValue(_countryId, out var country) || country.Government is not { } g)
            return;
        var player = snapshot.PlayerCountry == _countryId;
        if (_pickingPost != null && g.Candidates.TryGetValue(_pickingPost, out var candidates) && !ReferenceEquals(candidates, _shownCandidates))
        {
            _shownCandidates = candidates;
            ShowPicker(_pickingPost, candidates);
        }
        if (ReferenceEquals(g, _shown) && player == _shownPlayer)
            return;
        _shown = g;
        _shownPlayer = player;
        var record = _data.Countries.Countries.First(c => c.Id == _countryId);
        _title.Text = "Правительство: " + record.Name;
        _subtitle.Text = $"{_data.Politics.Systems.GetValueOrDefault(WorldFactory.SystemId(country.System), "")} · " +
                         (_data.Politics.Regimes?.GetValueOrDefault(WorldFactory.RegimeId(country.Regime)) ?? "") +
                         (player ? "" : " · вы можете только смотреть: это не ваша страна");

        var leader = g.Leader;
        _leaderPortrait.Show(leader.Female, leader.Age, leader.Id);
        _leaderRole.Text = g.LeaderTitle.ToUpperInvariant() + (player ? " · ЭТО ВЫ" : "");
        _leaderName.Text = leader.Name;
        _leaderInfo.Text = $"{Fmt.Age(leader.Age)}{TraitsText(leader)}";
        _leaderCompetence.Show("Компетентность", leader.Competence, UiTheme.Accent);
        _leaderPopularity.Show("Популярность", leader.Popularity, Color.FromHtml("#7fc49a"));
        _leaderCard.TooltipText = PersonTooltip(g.LeaderTitle, leader);

        var p = g.Parliament;
        _parliamentName.Text = (p.Kind == "none" ? "ПАРЛАМЕНТА НЕТ" : p.Name.ToUpperInvariant());
        _chart.Show(p);
        _chart.Visible = p.Seats > 0;
        _parliamentLegend.Text = p.Seats > 0
            ? $"Правящий блок {Fmt.Number(p.Ruling, 0)} · оппозиция {Fmt.Number(p.Opposition, 0)} · всего {Fmt.Number(p.Seats, 0)}"
            : "";
        _parliamentNote.Text = p.Kind switch
        {
            "real" when p.Ruling * 2 > p.Seats => "Парламент голосует всерьёз. У правительства большинство, но часть депутатов может проголосовать против непопулярного закона.",
            "real" => "Парламент голосует всерьёз, и у правительства нет большинства: законы пройдут, только если их поддержит часть оппозиции.",
            "formal" => "Парламент формальный: он одобряет почти всё, что предлагает власть.",
            _ => "Власть правит указами: законы и бюджет вступают в силу без голосования.",
        };

        foreach (var m in g.Ministers)
            if (_cards.TryGetValue(m.Post, out var card))
                card.Show(m.Person, Fmt.Age(m.Person.Age) + TraitsText(m.Person), PersonTooltip(_posts[m.Post].Name, m.Person), player);
    }

    /// <summary>Открыть выбор замены на пост: игра подберёт кандидатов.</summary>
    public void ChooseReplacement(string post)
    {
        if (_countryId == null)
            return;
        _pickingPost = post;
        _shownCandidates = null;
        _pickerTitle.Text = "Замена: " + _posts[post].Name.ToLowerInvariant();
        foreach (var child in _pickerRow.GetChildren())
            child.QueueFree();
        _pickerRow.AddChild(UiTheme.Text("Подбираем кандидатов…", 14, null, UiTheme.TextMuted));
        _picker.Visible = true;
        CandidatesRequested?.Invoke(_countryId, post);
    }

    void ShowPicker(string post, IReadOnlyList<CharacterSnapshot> candidates)
    {
        foreach (var child in _pickerRow.GetChildren())
            child.QueueFree();
        for (var i = 0; i < candidates.Count; i++)
        {
            var index = i;
            var c = candidates[i];
            var card = new PanelContainer { CustomMinimumSize = new Vector2(250, 0), MouseFilter = MouseFilterEnum.Stop };
            card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.05f), UiTheme.PanelBorder, 12, 12, 12));
            card.TooltipText = PersonTooltip(_posts[post].Name, c);
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 5);
            card.AddChild(column);
            var portrait = new Portrait { CustomMinimumSize = new Vector2(84, 108), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
            portrait.Show(c.Female, c.Age, c.Id);
            column.AddChild(portrait);
            column.AddChild(UiTheme.Text(c.Name, 16, UiTheme.SemiBold));
            var info = UiTheme.Text(Fmt.Age(c.Age) + TraitsText(c), 12, null, UiTheme.TextSecondary);
            info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            column.AddChild(info);
            foreach (var (label, value, color) in Qualities(c))
            {
                var meter = new Meter();
                meter.Show(label, value, color);
                column.AddChild(meter);
            }
            var appoint = new Button { Text = "Назначить", FocusMode = FocusModeEnum.None };
            appoint.AddThemeColorOverride("font_color", UiTheme.Ocean);
            appoint.AddThemeColorOverride("font_hover_color", UiTheme.Ocean);
            appoint.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.Accent, null, 8, 12, 6));
            appoint.AddThemeStyleboxOverride("hover", UiTheme.Box(UiTheme.AccentLight, null, 8, 12, 6));
            appoint.Pressed += () =>
            {
                if (_countryId == null)
                    return;
                Appointed?.Invoke(_countryId, post, index);
                _pickingPost = null;
                _picker.Visible = false;
            };
            column.AddChild(appoint);
            _pickerRow.AddChild(card);
        }
    }

    public static (string Label, double Value, Color Color)[] Qualities(CharacterSnapshot c) =>
    [
        ("Компетентность", c.Competence, UiTheme.Accent),
        ("Лояльность", c.Loyalty, Color.FromHtml("#6f8fb8")),
        ("Честность", 100 - c.Corruption, Color.FromHtml("#7fc49a")),
    ];

    string TraitsText(CharacterSnapshot c) =>
        c.Traits.Count == 0 ? "" : " · " + string.Join(", ", c.Traits.Select(t => _traits.TryGetValue(t, out var r) ? r.Name : t));

    string PersonTooltip(string role, CharacterSnapshot c)
    {
        var lines = new List<string>
        {
            $"{role}: {c.Name}",
            $"{Fmt.Age(c.Age)}, на посту с {Fmt.Date(c.Since)}",
            $"Компетентность {Fmt.Number(c.Competence, 0)} — как быстро и хорошо работает ведомство",
            $"Лояльность {Fmt.Number(c.Loyalty, 0)} — насколько верен главе",
            $"Честность {Fmt.Number(100 - c.Corruption, 0)}, популярность {Fmt.Number(c.Popularity, 0)}",
        };
        foreach (var t in c.Traits)
            if (_traits.TryGetValue(t, out var trait))
                lines.Add($"{trait.Name}: {trait.Description}");
        return string.Join('\n', lines);
    }
}
