using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Экран «Политика»: парламент в цветах партий, опросы по месяцам, карточки партий (позиция на трёх осях, лидер,
/// кто за неё голосует) и взгляды классов — за кого каждый класс голосует чаще всего.
/// </summary>
public partial class PoliticsScreen : GlassPanel
{
    static readonly Dictionary<string, string> ElectoralNames = new()
    {
        ["majoritarian"] = "мажоритарная система: побеждает первый в округе",
        ["proportional"] = "пропорциональная система",
        ["mixed"] = "смешанная система: половина мест по округам, половина по спискам",
        ["none"] = "выборов нет",
    };

    GameData _data = null!;
    Label _title = null!;
    Label _subtitle = null!;
    Label _parliamentName = null!;
    ParliamentChart _chart = null!;
    VBoxContainer _factions = null!;
    Label _rating = null!;
    PollChart _polls = null!;
    GridContainer _parties = null!;
    VBoxContainer _views = null!;
    Label _empty = null!;
    Control _content = null!;
    string? _countryId;
    PoliticsSnapshot? _shown;
    GovernmentSnapshot? _shownGovernment;

    public event Action? Closed;

    public string? CountryId => _countryId;

    public void Setup(GameData data)
    {
        _data = data;
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

        _empty = UiTheme.Text("", 15, null, UiTheme.TextSecondary);
        _empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_empty);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(content);
        _content = scroll;

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 14);
        content.AddChild(top);
        top.AddChild(BuildParliament());
        top.AddChild(BuildPolls());

        content.AddChild(UiTheme.Text("ПАРТИИ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        _parties = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _parties.AddThemeConstantOverride("h_separation", 10);
        _parties.AddThemeConstantOverride("v_separation", 10);
        content.AddChild(_parties);

        content.AddChild(UiTheme.Text("ВЗГЛЯДЫ КЛАССОВ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        var hint = UiTheme.Text("Где на трёх осях стоит каждый класс (светлая точка — средние взгляды страны) и за какую партию он чаще голосует. " +
                                "Кризис и недовольство сдвигают взгляды к перераспределению, порядку и суверенитету, радикализм — к краям.",
            12, null, UiTheme.TextMuted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(hint);
        _views = new VBoxContainer();
        _views.AddThemeConstantOverride("separation", 6);
        content.AddChild(_views);
    }

    static PanelContainer Card(float width = 0)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.035f), UiTheme.Hairline, 12, 14, 12));
        return card;
    }

    Control BuildParliament()
    {
        var card = Card(380);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        card.AddChild(column);
        _parliamentName = UiTheme.Text("", 11, UiTheme.MapCountryFont, UiTheme.Accent);
        column.AddChild(_parliamentName);
        _chart = new ParliamentChart { CustomMinimumSize = new Vector2(350, 150) };
        column.AddChild(_chart);
        _factions = new VBoxContainer();
        _factions.AddThemeConstantOverride("separation", 2);
        column.AddChild(_factions);
        return card;
    }

    Control BuildPolls()
    {
        var card = Card();
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        card.AddChild(column);
        var header = new HBoxContainer();
        column.AddChild(header);
        var caption = UiTheme.Text("ОПРОСЫ ПО МЕСЯЦАМ", 11, UiTheme.MapCountryFont, UiTheme.Accent);
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(caption);
        _rating = UiTheme.Text("", 13, UiTheme.Medium, UiTheme.TextSecondary);
        header.AddChild(_rating);
        _polls = new PollChart { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(_polls);
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

    public void Refresh(WorldSnapshot snapshot)
    {
        if (_countryId == null || !snapshot.Countries.TryGetValue(_countryId, out var country))
            return;
        if (_shown != null && ReferenceEquals(country.Politics, _shown) && ReferenceEquals(country.Government, _shownGovernment))
            return;
        _shown = country.Politics;
        _shownGovernment = country.Government;
        var record = _data.Countries.Countries.First(c => c.Id == _countryId);
        _title.Text = "Политика: " + record.Name;
        var player = snapshot.PlayerCountry == _countryId;
        if (country.Politics is not { Parties.Count: > 0 } p)
        {
            _subtitle.Text = player ? "" : "вы можете только смотреть: это не ваша страна";
            _empty.Text = "В стране нет легальных партий и выборов: власть не зависит от голосования. Недовольство классов здесь выливается " +
                          "не в опросы, а в протесты.";
            _empty.Visible = true;
            _content.Visible = false;
            return;
        }
        _empty.Visible = false;
        _content.Visible = true;
        var electoral = ElectoralNames.GetValueOrDefault(p.Electoral, p.Electoral);
        if (p.Electoral is "proportional" or "mixed")
            electoral += $", барьер {Fmt.Number(p.Threshold, p.Threshold == Math.Round(p.Threshold) ? 0 : 1)}%";
        _subtitle.Text = char.ToUpperInvariant(electoral[0]) + electoral[1..] +
                         (country.Regime == Sim.Model.Regime.Democracy ? "" : " · выборы проходят с перекосом в пользу власти") +
                         (player ? "" : " · вы можете только смотреть: это не ваша страна");
        RenderParliament(country, p);
        RenderPolls(country, p);
        RenderParties(country, p);
        RenderViews(country, p);
    }

    void RenderParliament(CountrySnapshot country, PoliticsSnapshot p)
    {
        foreach (var child in _factions.GetChildren())
            child.QueueFree();
        if (country.Government?.Parliament is not { Seats: > 0 } parliament)
        {
            _parliamentName.Text = "ПАРЛАМЕНТА НЕТ";
            _chart.Visible = false;
            return;
        }
        _chart.Visible = true;
        _parliamentName.Text = $"{parliament.Name.ToUpperInvariant()} · {Fmt.Number(parliament.Seats, 0)} МЕСТ";
        _chart.Show(parliament, p);
        foreach (var party in p.Parties.OrderByDescending(x => x.Seats))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(Dot(party.Color));
            var name = UiTheme.Text(party.Short, 13, null, UiTheme.TextSecondary);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.TooltipText = party.Name;
            name.MouseFilter = MouseFilterEnum.Pass;
            row.AddChild(name);
            if (party.Ruling)
                row.AddChild(UiTheme.Text(party.Head ? "глава" : "в коалиции", 11, UiTheme.Medium, UiTheme.Accent));
            var seats = UiTheme.Text(Fmt.Number(party.Seats, 0), 13, UiTheme.Numbers, UiTheme.TextPrimary);
            seats.HorizontalAlignment = HorizontalAlignment.Right;
            seats.CustomMinimumSize = new Vector2(48, 0);
            row.AddChild(seats);
            _factions.AddChild(row);
        }
        var majority = parliament.Ruling * 2 > parliament.Seats;
        var note = UiTheme.Text(majority
                ? $"У правящих {Fmt.Number(parliament.Ruling, 0)} мест из {Fmt.Number(parliament.Seats, 0)} — большинство."
                : $"У правящих {Fmt.Number(parliament.Ruling, 0)} мест из {Fmt.Number(parliament.Seats, 0)} — правительство меньшинства: " +
                  "законы пройдут, только если их поддержит часть оппозиции.",
            12, null, UiTheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _factions.AddChild(note);
    }

    void RenderPolls(CountrySnapshot country, PoliticsSnapshot p)
    {
        var dates = p.Polls.Select(x => Fmt.MonthYear(x.Date)).ToList();
        var series = p.Parties
            .Select(party => new PollChart.Series(party.Short, UiTheme.Hex(party.Color),
                p.Polls.Select(x => x.Shares.TryGetValue(party.Id, out var v) ? v : double.NaN).ToList()))
            .ToList();
        _polls.Show(dates, series);
        var rating = country.Stat("ruling_poll_pct")?.Value ?? double.NaN;
        _rating.Text = double.IsFinite(rating) ? $"правящие партии: {Fmt.Value("percent", rating)}" : "";
        _rating.TooltipText = _data.Indicator("ruling_poll_pct").Description;
        _rating.MouseFilter = MouseFilterEnum.Pass;
    }

    void RenderParties(CountrySnapshot country, PoliticsSnapshot p)
    {
        foreach (var child in _parties.GetChildren())
            child.QueueFree();
        var mean = Mean(country, p);
        foreach (var party in p.Parties)
            _parties.AddChild(PartyCard(country, party, mean));
    }

    Control PartyCard(CountrySnapshot country, PartySnapshot party, double[] mean)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(320, 0), MouseFilter = MouseFilterEnum.Pass };
        var box = UiTheme.Box(new Color(1, 1, 1, 0.035f), UiTheme.Hairline, 12, 14, 10);
        box.BorderColor = UiTheme.Hex(party.Color);
        (box.BorderWidthLeft, box.BorderWidthTop, box.BorderWidthRight, box.BorderWidthBottom) = (4, 0, 0, 0);
        card.AddThemeStyleboxOverride("panel", box);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 3);
        card.AddChild(column);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        column.AddChild(top);
        var name = UiTheme.Text(party.Name, 15, UiTheme.SemiBold);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        top.AddChild(name);
        var poll = UiTheme.Text(Fmt.Value("percent", party.Poll * 100), 20, UiTheme.Numbers, UiTheme.TextPrimary);
        poll.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        poll.TooltipText = "Доля в опросах среди определившихся избирателей";
        poll.MouseFilter = MouseFilterEnum.Pass;
        top.AddChild(poll);

        var status = party.Head ? "партия главы" : party.Ruling ? "в правящей коалиции" : party.Loyal ? "лояльная оппозиция" : "оппозиция";
        var meta = UiTheme.Text($"{_data.Family(party.Family).Name} · {party.Short} · с {party.Founded} года · мест: {Fmt.Number(party.Seats, 0)}",
            12, null, UiTheme.TextMuted);
        meta.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(meta);
        column.AddChild(UiTheme.Text(status + (party.Power ? " · партия власти" : ""), 12, UiTheme.Medium,
            party.Ruling ? UiTheme.Accent : UiTheme.TextSecondary));

        var leader = party.Leader ?? country.Government?.Leader;
        if (leader != null)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var portrait = new Portrait { CustomMinimumSize = new Vector2(30, 38) };
            portrait.Show(leader.Female, leader.Age, leader.Id);
            row.AddChild(portrait);
            var text = UiTheme.Text($"{leader.Name}, {Fmt.Age(leader.Age)}\n" + (party.Head ? "лидер партии и глава исполнительной власти" : "лидер партии") +
                                    $" · популярность {Fmt.Number(leader.Popularity, 0)}", 12, null, UiTheme.TextSecondary);
            text.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(text);
            column.AddChild(row);
        }

        for (var a = 0; a < _data.Parties.Axes.Count; a++)
        {
            var scale = new AxisScale { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            scale.Show(_data.Parties.Axes[a], party.Position[a], UiTheme.Hex(party.Color), mean[a]);
            column.AddChild(scale);
        }
        var classes = _data.Classes.Classes;
        var voters = Enumerable.Range(0, Math.Min(classes.Count, party.Voters.Count)).OrderByDescending(k => party.Voters[k]).Take(3)
            .Select(k => $"{(classes[k].ShortName ?? classes[k].Name).ToLowerInvariant()} {Fmt.Number(party.Voters[k] * 100, 0)}%");
        var who = UiTheme.Text("Голосуют: " + string.Join(", ", voters), 12, null, UiTheme.TextMuted);
        who.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(who);
        return card;
    }

    void RenderViews(CountrySnapshot country, PoliticsSnapshot p)
    {
        foreach (var child in _views.GetChildren())
            child.QueueFree();
        var classes = _data.Classes.Classes;
        var mean = Mean(country, p);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        var spacer = new Control { CustomMinimumSize = new Vector2(190, 0) };
        header.AddChild(spacer);
        foreach (var axis in _data.Parties.Axes)
        {
            var label = UiTheme.Text(axis.Name.ToUpperInvariant(), 10, UiTheme.MapCountryFont, UiTheme.TextMuted);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            header.AddChild(label);
        }
        header.AddChild(new Control { CustomMinimumSize = new Vector2(150, 0) });
        _views.AddChild(header);
        for (var k = 0; k < classes.Count && k < p.Views.Count; k++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var name = UiTheme.Text(classes[k].ShortName ?? classes[k].Name, 13, null, UiTheme.TextSecondary);
            name.CustomMinimumSize = new Vector2(190, 0);
            name.ClipText = true;
            row.AddChild(name);
            for (var a = 0; a < _data.Parties.Axes.Count; a++)
            {
                var scale = new AxisScale { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                scale.Show(_data.Parties.Axes[a], p.Views[k][a], UiTheme.Hex(classes[k].Color), mean[a]);
                row.AddChild(scale);
            }
            // За кого класс голосует чаще: доля партии в опросе × доля класса среди её избирателей.
            var best = p.Parties.OrderByDescending(x => x.Poll * (k < x.Voters.Count ? x.Voters[k] : 0)).First();
            var total = p.Parties.Sum(x => x.Poll * (k < x.Voters.Count ? x.Voters[k] : 0));
            var share = total > 0 ? best.Poll * best.Voters[k] / total * 100 : 0;
            var vote = new HBoxContainer { CustomMinimumSize = new Vector2(150, 0) };
            vote.AddThemeConstantOverride("separation", 6);
            vote.AddChild(Dot(best.Color));
            var text = UiTheme.Text($"{best.Short} {Fmt.Number(share, 0)}%", 12, UiTheme.Medium, UiTheme.TextSecondary);
            text.ClipText = true;
            text.TooltipText = $"{classes[k].Name}: чаще всего голосуют за «{best.Name}»";
            text.MouseFilter = MouseFilterEnum.Pass;
            vote.AddChild(text);
            row.AddChild(vote);
            _views.AddChild(row);
        }
    }

    /// <summary>Средние взгляды взрослых: классы взвешены их долей среди избирателей (по опросам).</summary>
    static double[] Mean(CountrySnapshot country, PoliticsSnapshot p)
    {
        var mean = new double[3];
        var weights = Enumerable.Range(0, p.Views.Count).Select(k => p.Parties.Sum(x => x.Poll * (k < x.Voters.Count ? x.Voters[k] : 0))).ToArray();
        var total = weights.Sum();
        if (total <= 0)
            return mean;
        for (var k = 0; k < p.Views.Count; k++)
            for (var a = 0; a < 3; a++)
                mean[a] += p.Views[k][a] * weights[k] / total;
        return mean;
    }

    static Control Dot(string color)
    {
        var dot = new Panel { CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        dot.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Hex(color), null, 5, 0, 0));
        return dot;
    }
}
