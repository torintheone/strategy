using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Экран «Дипломатия» (страна игрока): слева — все страны с отношениями и долей в нашей торговле, справа — выбранный
/// партнёр: из чего сложились отношения, договоры (действующие и те, что можно предложить, с условием согласия),
/// визит, помощь, высылка дипломатов.
/// </summary>
public partial class DiplomacyScreen : GlassPanel
{
    static readonly Color Good = Color.FromHtml("#7fc49a");

    GameData _data = null!;
    Label _title = null!;
    Label _subtitle = null!;
    VBoxContainer _list = null!;
    VBoxContainer _detail = null!;
    string? _partner;
    bool _byTrade;
    string? _shown;
    DiplomacySnapshot? _snapshot;

    public event Action? Closed;
    /// <summary>Действие: партнёр, что сделать (visit, aid, expel, propose:тип, cancel:тип).</summary>
    public event Action<string, string>? ActionChosen;

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

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 20);
        column.AddChild(body);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(430, 0) };
        left.AddThemeConstantOverride("separation", 8);
        body.AddChild(left);
        var sort = new HBoxContainer();
        sort.AddThemeConstantOverride("separation", 6);
        left.AddChild(sort);
        sort.AddChild(SortButton("По отношениям", false));
        sort.AddChild(SortButton("По торговле", true));
        var listScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        left.AddChild(listScroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 1);
        listScroll.AddChild(_list);

        var detailScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        body.AddChild(detailScroll);
        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 10);
        detailScroll.AddChild(_detail);
    }

    Button SortButton(string text, bool byTrade)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += () =>
        {
            _byTrade = byTrade;
            _shown = null;
            Render();
        };
        return button;
    }

    /// <summary>Открыть экран; partner — страна, которую показать сразу (например, выбранная на карте).</summary>
    public void Open(WorldSnapshot snapshot, string? partner)
    {
        if (partner != null && partner != snapshot.PlayerCountry)
            _partner = partner;
        _shown = null;
        var appearing = !Visible;
        Visible = true;
        Refresh(snapshot);
        if (appearing)
            AnimateIn(new Vector2(0, 0.1f), 0.2f, 0.985f);
    }

    public void Refresh(WorldSnapshot snapshot)
    {
        if (snapshot.Diplomacy is not { } diplomacy)
            return;
        // Перерисовываем, когда что-то изменилось: месяц, отношения с выбранным, договоры.
        var selected = diplomacy.Partners.FirstOrDefault(p => p.Id == _partner);
        var signature = $"{snapshot.Date:yyyy-MM}|{_partner}|{_byTrade}|{selected?.Relations:F2}|{string.Join(",", selected?.Treaties ?? [])}|{selected?.CanVisit}";
        if (signature == _shown)
            return;
        _shown = signature;
        _snapshot = diplomacy;
        Render();
    }

    void Render()
    {
        if (_snapshot is not { } diplomacy)
            return;
        var name = CountryName(diplomacy.Country);
        _title.Text = "Дипломатия: " + name;
        var friends = diplomacy.Partners.Count(p => p.Relations > 30);
        var enemies = diplomacy.Partners.Count(p => p.Relations < -30);
        var treaties = diplomacy.Partners.Sum(p => p.Treaties.Count);
        _subtitle.Text = $"Друзей (отношения выше +30): {friends} · недругов (ниже −30): {enemies} · договоров: {treaties}. Выберите страну слева.";
        _partner ??= diplomacy.Partners.MaxBy(p => p.TradeShare)?.Id;
        RenderList(diplomacy);
        RenderDetail(diplomacy);
    }

    void RenderList(DiplomacySnapshot diplomacy)
    {
        Clear(_list);
        var ordered = _byTrade ? diplomacy.Partners.OrderByDescending(p => p.TradeShare) : diplomacy.Partners.OrderByDescending(p => p.Relations);
        foreach (var p in ordered)
        {
            var row = new Button { Flat = true, FocusMode = FocusModeEnum.None, ToggleMode = true, ButtonPressed = p.Id == _partner };
            row.CustomMinimumSize = new Vector2(0, 28);
            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            line.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            line.OffsetLeft = 8;
            line.OffsetRight = -8;
            line.AddThemeConstantOverride("separation", 8);
            row.AddChild(line);
            var label = UiTheme.Text(CountryName(p.Id), 14, null, UiTheme.TextPrimary);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MouseFilter = MouseFilterEnum.Ignore;
            line.AddChild(label);
            var trade = UiTheme.Text(p.TradeShare >= 0.001 ? Fmt.Value("percent", p.TradeShare * 100) : "", 12, UiTheme.Numbers, UiTheme.TextMuted);
            trade.VerticalAlignment = VerticalAlignment.Center;
            trade.MouseFilter = MouseFilterEnum.Ignore;
            line.AddChild(trade);
            var value = UiTheme.Text(Fmt.Signed(p.Relations, 0), 14, UiTheme.Numbers, Tone(p.Relations));
            value.CustomMinimumSize = new Vector2(48, 0);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.VerticalAlignment = VerticalAlignment.Center;
            value.MouseFilter = MouseFilterEnum.Ignore;
            line.AddChild(value);
            var id = p.Id;
            row.Pressed += () =>
            {
                _partner = id;
                _shown = null;
                Render();
            };
            _list.AddChild(row);
        }
    }

    void RenderDetail(DiplomacySnapshot diplomacy)
    {
        Clear(_detail);
        if (diplomacy.Partners.FirstOrDefault(p => p.Id == _partner) is not { } p)
            return;
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 16);
        var name = UiTheme.Text(CountryName(p.Id), 24, UiTheme.SerifBold);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(name);
        head.AddChild(UiTheme.Text("отношения " + Fmt.Signed(p.Relations, 0), 22, UiTheme.SemiBold, Tone(p.Relations)));
        _detail.AddChild(head);
        _detail.AddChild(Wrapped($"Доля в нашей торговле — {Fmt.Value("percent", p.TradeShare * 100)}. " +
                                 "Отношения складываются из торговли, сходства строя и взглядов правящих партий, соседства, истории, договоров и событий; " +
                                 "события забываются за несколько лет.", 13, UiTheme.TextSecondary));
        var factors = new GridContainer { Columns = 2 };
        factors.AddThemeConstantOverride("h_separation", 24);
        factors.AddThemeConstantOverride("v_separation", 2);
        foreach (var f in p.Factors)
        {
            factors.AddChild(UiTheme.Text(f.Label, 13, null, UiTheme.TextSecondary));
            var v = UiTheme.Text(Fmt.Signed(f.Value, 0), 13, UiTheme.Numbers, Tone(f.Value));
            v.HorizontalAlignment = HorizontalAlignment.Right;
            factors.AddChild(v);
        }
        _detail.AddChild(factors);

        _detail.AddChild(UiTheme.Text("ДЕЙСТВИЯ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        var b = _data.DiplomacyBalance;
        Action("Визит главы", $"Отношения +{b.VisitRelations:0}. К одной стране — не чаще раза в {b.VisitMonths} месяцев; визит к недругу (ниже {b.VisitCriticismBelow:0}) критикуют дома.",
            p.CanVisit ? "Поехать" : null, p.CanVisit ? null : "Вы недавно были там с визитом.", "visit");
        Action("Помощь", $"Подарить {Fmt.Number(b.AidShare, 2)}% нашего ВВП: наш долг растёт, их — уменьшается. Чем заметнее помощь для их экономики, тем сильнее растут отношения.",
            "Помочь", null, "aid");
        Action("Выслать дипломатов", $"Сигнал недовольства: отношения −{b.ExpelRelations:0}.", "Выслать", null, "expel", critical: true);

        _detail.AddChild(UiTheme.Text("ДОГОВОРЫ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        foreach (var t in _data.Diplomacy.Treaties)
        {
            if (p.Treaties.Contains(t.Id))
                Action(t.Name, t.Description + " Действует.", "Расторгнуть", null, "cancel:" + t.Id, active: true);
            else
            {
                var (accepts, reason) = p.Offers.TryGetValue(t.Id, out var offer) ? offer : (false, "");
                Action(t.Name, t.Description + " " + reason, accepts ? "Предложить" : null, accepts ? null : "Не согласятся.", "propose:" + t.Id);
            }
        }
    }

    void Action(string title, string text, string? button, string? disabled, string action, bool critical = false, bool active = false)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 0);
        column.AddChild(UiTheme.Text(title, 14, UiTheme.SemiBold, active ? Good : UiTheme.TextPrimary));
        column.AddChild(Wrapped(text, 12, UiTheme.TextMuted));
        row.AddChild(column);
        if (button != null)
        {
            var b = new Button { Text = button, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(130, 30) };
            b.AddThemeFontSizeOverride("font_size", 13);
            if (critical)
                b.AddThemeColorOverride("font_color", UiTheme.Critical);
            b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            b.Pressed += () =>
            {
                if (_partner != null)
                    ActionChosen?.Invoke(_partner, action);
            };
            row.AddChild(b);
        }
        else if (disabled != null)
        {
            var note = UiTheme.Text(disabled, 12, null, UiTheme.TextMuted);
            note.CustomMinimumSize = new Vector2(130, 0);
            note.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            note.HorizontalAlignment = HorizontalAlignment.Center;
            row.AddChild(note);
        }
        _detail.AddChild(row);
    }

    string CountryName(string id) => _data.Countries.Countries.FirstOrDefault(c => c.Id == id)?.Name ?? id;

    static Color Tone(double value) => value >= 15 ? Good : value <= -15 ? UiTheme.Critical : UiTheme.TextPrimary;

    static Label Wrapped(string text, int size, Color color)
    {
        var label = UiTheme.Text(text, size, null, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
            child.QueueFree();
    }
}
