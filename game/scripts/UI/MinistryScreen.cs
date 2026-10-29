using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Экран «Ведомства»: слева 18 министерств и служб, справа выбранное — министр и его доклад, четыре показателя
/// с графиками за 10 лет, статьи бюджета ведомства и законы, которые оно готовит. Из экрана можно перейти к замене
/// министра, к закону и к бюджету.
/// </summary>
public partial class MinistryScreen : GlassPanel
{
    const int ChartYears = 10;
    static readonly Color ChartColor = Color.FromHtml("#6f9fd8");

    GameData _data = null!;
    Dictionary<string, TraitRecord> _traits = null!;
    Label _title = null!;
    Label _subtitle = null!;
    readonly Dictionary<string, Button> _rows = new();
    Label _postTitle = null!;
    Label _postMeta = null!;
    Portrait _portrait = null!;
    Label _ministerName = null!;
    Label _ministerInfo = null!;
    readonly Meter[] _meters = [new(), new(), new()];
    Button _replace = null!;
    Control _ministerCard = null!;
    VBoxContainer _report = null!;
    readonly List<(Control Card, Label Name, Label Value, Label Change, LineChart Chart, Label Note)> _charts = new();
    VBoxContainer _budget = null!;
    Button _openBudget = null!;
    VBoxContainer _laws = null!;
    string? _countryId;
    string _postId = "";
    CountrySnapshot? _shown;
    bool _shownPlayer;
    (int Year, int Month) _shownMonth;
    WorldSnapshot? _snapshot;

    public event Action? Closed;
    public event Action<string, string>? ReplaceRequested;
    public event Action<string, string>? LawRequested;
    public event Action? BudgetRequested;

    public string? CountryId => _countryId;

    public void Setup(GameData data)
    {
        _data = data;
        _traits = data.Government.Traits.ToDictionary(t => t.Id);
        _postId = data.Government.Posts[0].Id;
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

        var split = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 18);
        column.AddChild(split);
        split.AddChild(BuildList());
        split.AddChild(BuildDetail());
        SelectPost(_postId);
    }

    Control BuildList()
    {
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(300, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(list);
        foreach (var post in _data.Government.Posts)
        {
            var button = new Button
            {
                Text = post.Ministry,
                Flat = true,
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true,
                CustomMinimumSize = new Vector2(0, 34),
            };
            button.AddThemeFontSizeOverride("font_size", 14);
            var id = post.Id;
            button.Pressed += () => SelectPost(id);
            list.AddChild(button);
            _rows[post.Id] = button;
        }
        return scroll;
    }

    Control BuildDetail()
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(column);
        _postTitle = UiTheme.Text("", 22, UiTheme.SerifBold);
        column.AddChild(_postTitle);
        _postMeta = UiTheme.Text("", 13, null, UiTheme.TextMuted);
        _postMeta.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_postMeta);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 14);
        column.AddChild(top);
        top.AddChild(BuildMinister());
        var report = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        report.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.035f), UiTheme.Hairline, 12, 14, 12));
        top.AddChild(report);
        var reportColumn = new VBoxContainer();
        reportColumn.AddThemeConstantOverride("separation", 6);
        report.AddChild(reportColumn);
        reportColumn.AddChild(UiTheme.Text("ДОКЛАД МИНИСТРА", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        _report = new VBoxContainer();
        _report.AddThemeConstantOverride("separation", 5);
        reportColumn.AddChild(_report);

        column.AddChild(UiTheme.Text($"ПОКАЗАТЕЛИ ЗА {ChartYears} ЛЕТ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        column.AddChild(grid);
        var most = _data.Government.Posts.Max(p => p.Stats?.Count ?? 0);
        for (var i = 0; i < most; i++)
            grid.AddChild(BuildChartCard());

        var budgetHeader = new HBoxContainer();
        column.AddChild(budgetHeader);
        var budgetCaption = UiTheme.Text("БЮДЖЕТ ВЕДОМСТВА", 11, UiTheme.MapCountryFont, UiTheme.Accent);
        budgetCaption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        budgetCaption.VerticalAlignment = VerticalAlignment.Center;
        budgetHeader.AddChild(budgetCaption);
        _openBudget = new Button { Text = "Открыть бюджет", Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Экран «Бюджет и налоги» (B)" };
        _openBudget.AddThemeFontSizeOverride("font_size", 13);
        _openBudget.AddThemeColorOverride("font_color", UiTheme.Accent);
        _openBudget.Pressed += () => BudgetRequested?.Invoke();
        budgetHeader.AddChild(_openBudget);
        _budget = new VBoxContainer();
        _budget.AddThemeConstantOverride("separation", 4);
        column.AddChild(_budget);

        column.AddChild(UiTheme.Text("ЗАКОНЫ ВЕДОМСТВА", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        _laws = new VBoxContainer();
        _laws.AddThemeConstantOverride("separation", 2);
        column.AddChild(_laws);
        return scroll;
    }

    Control BuildMinister()
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(330, 0), MouseFilter = MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.035f), UiTheme.Hairline, 12, 14, 12));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        card.AddChild(row);
        _portrait = new Portrait { CustomMinimumSize = new Vector2(84, 108), SizeFlagsVertical = SizeFlags.ShrinkBegin };
        row.AddChild(_portrait);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 3);
        row.AddChild(column);
        _ministerName = UiTheme.Text("", 17, UiTheme.SemiBold);
        _ministerName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_ministerName);
        _ministerInfo = UiTheme.Text("", 12, null, UiTheme.TextSecondary);
        _ministerInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_ministerInfo);
        foreach (var m in _meters)
            column.AddChild(m);
        _replace = new Button { Text = "Сменить министра", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _replace.AddThemeFontSizeOverride("font_size", 13);
        _replace.AddThemeColorOverride("font_color", UiTheme.Accent);
        _replace.Pressed += () =>
        {
            if (_countryId != null)
                ReplaceRequested?.Invoke(_countryId, _postId);
        };
        column.AddChild(_replace);
        _ministerCard = card;
        return card;
    }

    Control BuildChartCard()
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.035f), UiTheme.Hairline, 12, 14, 10));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        card.AddChild(column);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        column.AddChild(top);
        var name = UiTheme.Text("", 13, UiTheme.Medium, UiTheme.TextSecondary);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.MouseFilter = MouseFilterEnum.Pass;
        top.AddChild(name);
        var value = UiTheme.Text("", 18, UiTheme.Numbers, UiTheme.TextPrimary);
        top.AddChild(value);
        var change = UiTheme.Text("", 12, null, UiTheme.TextMuted);
        change.HorizontalAlignment = HorizontalAlignment.Right;
        column.AddChild(change);
        var chart = new LineChart { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(chart);
        var note = UiTheme.Text("", 12, null, UiTheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(note);
        _charts.Add((card, name, value, change, chart, note));
        return card;
    }

    public void ShowCountry(string countryId, WorldSnapshot snapshot)
    {
        _countryId = countryId;
        _shown = null;
        var appearing = !Visible;
        Visible = true;
        Refresh(snapshot);
        if (appearing)
            AnimateIn(new Vector2(0, 0.1f), 0.2f, 0.985f);
    }

    /// <summary>Выбрать ведомство (для обхода скриншотов и переходов с других экранов).</summary>
    public void SelectPost(string postId)
    {
        _postId = postId;
        foreach (var (id, button) in _rows)
            button.SetPressedNoSignal(id == postId);
        if (_shown != null && _snapshot != null)
            Render(_shown, _snapshot, _shownPlayer);
    }

    /// <summary>Перерисовка — при смене месяца (показатели считаются помесячно), состава правительства или законов.</summary>
    public void Refresh(WorldSnapshot snapshot)
    {
        if (_countryId == null || !snapshot.Countries.TryGetValue(_countryId, out var country))
            return;
        var player = snapshot.PlayerCountry == _countryId;
        var month = (snapshot.Date.Year, snapshot.Date.Month);
        if (_shown != null && month == _shownMonth && player == _shownPlayer && ReferenceEquals(country.Government, _shown.Government)
            && ReferenceEquals(country.Laws, _shown.Laws) && ReferenceEquals(country.History, _shown.History))
            return;
        _shown = country;
        _shownPlayer = player;
        _shownMonth = month;
        _snapshot = snapshot;
        var record = _data.Countries.Countries.First(c => c.Id == _countryId);
        _title.Text = "Ведомства: " + record.Name;
        _subtitle.Text = $"{_data.Government.Posts.Count} министерств и служб · доклады на {Fmt.Date(snapshot.Date)}" +
                         (player ? "" : " · вы можете только смотреть: это не ваша страна");
        Render(country, snapshot, player);
    }

    void Render(CountrySnapshot country, WorldSnapshot snapshot, bool player)
    {
        var post = _data.Government.Posts.First(p => p.Id == _postId);
        var minister = country.Government?.Minister(post.Id);
        foreach (var p in _data.Government.Posts)
            _rows[p.Id].TooltipText = country.Government?.Minister(p.Id) is { } m ? $"{p.Name}: {m.Name}" : p.Name;

        _postTitle.Text = post.Ministry;
        _postMeta.Text = minister != null
            ? $"{post.Name}: {minister.Name}, на посту с {Fmt.Date(minister.Since)}"
            : post.Name + ": пост свободен";

        _ministerCard.Visible = minister != null;
        if (minister != null)
        {
            _portrait.Show(minister.Female, minister.Age, minister.Id);
            _ministerName.Text = minister.Name;
            _ministerInfo.Text = Fmt.Age(minister.Age) + (minister.Traits.Count == 0 ? ""
                : " · " + string.Join(", ", minister.Traits.Select(t => _traits.TryGetValue(t, out var r) ? r.Name : t)));
            var qualities = GovernmentScreen.Qualities(minister);
            for (var i = 0; i < _meters.Length; i++)
                _meters[i].Show(qualities[i].Label, qualities[i].Value, qualities[i].Color);
            _ministerCard.TooltipText = string.Join('\n', minister.Traits
                .Where(_traits.ContainsKey).Select(t => $"{_traits[t].Name}: {_traits[t].Description}"));
        }
        _replace.Visible = player && minister != null;

        foreach (var child in _report.GetChildren())
            child.QueueFree();
        foreach (var line in MinistryReport.Lines(_data, post, country, snapshot, minister))
        {
            var label = UiTheme.Text("• " + line, 14, null, UiTheme.TextSecondary);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _report.AddChild(label);
        }

        RenderCharts(post, country, snapshot.Date);
        RenderBudget(post, country, player);
        RenderLaws(post, country);
    }

    void RenderCharts(PostRecord post, CountrySnapshot country, DateOnly date)
    {
        var stats = post.Stats ?? [];
        for (var i = 0; i < _charts.Count; i++)
        {
            var (card, name, value, change, chart, note) = _charts[i];
            card.Visible = i < stats.Count;
            if (i >= stats.Count)
                continue;
            var def = _data.Indicator(stats[i]);
            name.Text = def.Name;
            name.TooltipText = def.Description;
            var now = country.Stat(def.Id)?.Value ?? double.NaN;
            value.Text = Fmt.Value(def.Format, now, def.Unit);
            var series = (country.History?.Series(def.Id) ?? []).Where(p => p.Year > date.Year - 1 - ChartYears).ToList();
            var points = series.Select(p => (p.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Value)).ToList();
            if (double.IsFinite(now))
                points.Add(("сейчас", now));
            chart.Show(points, def.Format, def.Unit, ChartColor, lastIsNow: double.IsFinite(now));
            change.Text = series.Count > 0 ? $"в {series[0].Year} году — {Fmt.Value(def.Format, series[0].Value, def.Unit)}" : "";
            note.Text = series.Count == 0 ? "Прошлых лет в данных нет: график начнётся с первого года игры." : "";
            note.Visible = series.Count == 0;
        }
    }

    void RenderBudget(PostRecord post, CountrySnapshot country, bool player)
    {
        foreach (var child in _budget.GetChildren())
            child.QueueFree();
        _openBudget.Visible = player;
        var items = _data.Budget.Spending;
        if (post.Spending.Count == 0)
        {
            var text = post.Finance ? "У ведомства нет своей статьи расходов: оно готовит весь бюджет страны."
                : post.Id == "central_bank" ? "Центробанк живёт на свои доходы и денег из бюджета не получает."
                : "У ведомства нет своей статьи расходов: его содержание входит в общие расходы на управление.";
            _budget.AddChild(UiTheme.Text(text, 13, null, UiTheme.TextMuted));
            return;
        }
        var total = country.Spending?.Sum() ?? 0;
        foreach (var id in post.Spending)
        {
            var k = Enumerable.Range(0, items.Count).First(i => items[i].Id == id);
            var share = country.Spending is { } s && k < s.Count ? s[k] : double.NaN;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var name = UiTheme.Text(items[k].Name, 14, null, UiTheme.TextSecondary);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(name);
            var ofTotal = total > 0 && double.IsFinite(share) ? $"{Fmt.Value("percent", share / total * 100)} всех расходов · " : "";
            row.AddChild(UiTheme.Text(ofTotal + Fmt.Value("percent_gdp", share * 100), 14, UiTheme.Numbers, UiTheme.TextPrimary));
            _budget.AddChild(row);
        }
    }

    void RenderLaws(PostRecord post, CountrySnapshot country)
    {
        foreach (var child in _laws.GetChildren())
            child.QueueFree();
        var laws = _data.Laws.Laws.Where(l => l.Post == post.Id).ToList();
        if (laws.Count == 0 || country.Laws is not { } state)
        {
            _laws.AddChild(UiTheme.Text("Ведомство не готовит законов: их вносят другие министерства.", 13, null, UiTheme.TextMuted));
            return;
        }
        foreach (var law in laws)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var button = new Button
            {
                Text = $"{law.Name}: {law.Variant(state.Current[law.Id]).Name.ToLowerInvariant()}",
                Flat = true,
                FocusMode = FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true,
                TooltipText = "Открыть закон на экране «Законы»",
            };
            button.AddThemeFontSizeOverride("font_size", 14);
            var id = law.Id;
            button.Pressed += () =>
            {
                if (_countryId != null)
                    LawRequested?.Invoke(_countryId, id);
            };
            row.AddChild(button);
            var status = state.BillFor(law.Id) != null ? "готовится"
                : state.Transitions.TryGetValue(law.Id, out var progress) ? $"вводится {Math.Round(progress * 100)}%"
                : state.Current[law.Id] != state.Start[law.Id] ? "изменён" : "";
            var label = UiTheme.Text(status, 12, UiTheme.Medium, UiTheme.Accent);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(label);
            _laws.AddChild(row);
        }
    }
}
