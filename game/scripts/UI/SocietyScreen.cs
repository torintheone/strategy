using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Экран «Общество»: протесты и силовики (лояльность, репрессии, аресты оппозиции), группы интересов — влияние,
/// одобрение власти и из чего оно сложилось, чем группа давит или помогает, её требования с переходом к закону или
/// бюджету — и классы (доля, доход, уровень жизни, настроение, радикализм, влияние).
/// </summary>
public partial class SocietyScreen : GlassPanel
{
    static readonly Color Good = Color.FromHtml("#7fc49a");

    GameData _data = null!;
    Label _title = null!;
    Label _subtitle = null!;
    GridContainer _classes = null!;
    GridContainer _groups = null!;
    VBoxContainer _unrest = null!;
    string? _countryId;
    PoliticsSnapshot? _shownPolitics;
    IReadOnlyList<ClassSnapshot>? _shownClasses;
    bool _player;

    public event Action? Closed;
    public event Action<string, string, string>? LawRequested;
    public event Action<string>? BudgetRequested;
    public event Action<string, int>? ProtestRequested;
    public event Action<string>? ArrestRequested;

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

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(content);

        _unrest = new VBoxContainer();
        _unrest.AddThemeConstantOverride("separation", 6);
        content.AddChild(_unrest);

        content.AddChild(UiTheme.Text("ГРУППЫ ИНТЕРЕСОВ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        content.AddChild(Wrapped("Влияние — доля группы в силе всех групп: от численности и богатства её классов, отраслей экономики, режима. " +
                                 "Одобрение — как группа относится к власти: ниже 40 она давит на власть, выше 60 — помогает. Довольные группы дают " +
                                 "деньги правящим партиям, недовольные — оппозиции; все лоббируют законы в парламенте.", 12, UiTheme.TextMuted));
        _groups = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _groups.AddThemeConstantOverride("h_separation", 10);
        _groups.AddThemeConstantOverride("v_separation", 10);
        content.AddChild(_groups);

        content.AddChild(UiTheme.Text("КЛАССЫ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        _classes = new GridContainer { Columns = 8, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _classes.AddThemeConstantOverride("h_separation", 18);
        _classes.AddThemeConstantOverride("v_separation", 4);
        content.AddChild(_classes);
    }

    public void ShowCountry(string countryId, WorldSnapshot snapshot)
    {
        _countryId = countryId;
        _shownPolitics = null;
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
        var player = snapshot.PlayerCountry == _countryId;
        if (_shownPolitics != null && ReferenceEquals(country.Politics, _shownPolitics) && ReferenceEquals(country.Classes, _shownClasses) && player == _player)
            return;
        (_shownPolitics, _shownClasses, _player) = (country.Politics, country.Classes, player);
        var record = _data.Countries.Countries.First(c => c.Id == _countryId);
        _title.Text = "Общество: " + record.Name;
        _subtitle.Text = player ? "Требования групп можно выполнить: кнопка ведёт к нужному закону или к бюджету" : "вы можете только смотреть: это не ваша страна";
        RenderUnrest(country, player);
        RenderGroups(country, player);
        RenderClasses(country);
    }

    /// <summary>Протесты (с кнопкой ответа в своей стране) и силовики: лояльность, репрессии, аресты оппозиции.</summary>
    void RenderUnrest(CountrySnapshot country, bool player)
    {
        Clear(_unrest);
        if (country.Politics is not { } p)
            return;
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        _unrest.AddChild(header);
        var caption = UiTheme.Text("ПРОТЕСТЫ И СИЛОВИКИ", 11, UiTheme.MapCountryFont, UiTheme.Accent);
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(caption);
        if (p.Security is { } sec)
        {
            var text = $"Лояльность полиции {Fmt.Number(sec.PoliceLoyalty, 0)}, армии {Fmt.Number(sec.ArmyLoyalty, 0)}" +
                       (sec.Repression >= 1 ? $" · репрессии {Fmt.Number(sec.Repression, 0)}" : "") +
                       $" · беспорядки {Fmt.Number(p.Unrest, 0)}";
            var label = UiTheme.Text(text, 13, UiTheme.Medium, sec.PoliceLoyalty < 35 || sec.ArmyLoyalty < 35 ? UiTheme.Critical : UiTheme.TextSecondary);
            label.TooltipText = "Лояльность силовиков (0–100) — от того, довольна ли властью группа «Силовики», и от нагрузки: каждый разгон изматывает " +
                                "полицию (беспорядки — армию). Нелояльные силовики хуже разгоняют протесты.\nРепрессии (0–100) — от разгонов и арестов: " +
                                "бьют по свободам и легитимности, растят радикализм, со временем забываются.\nБеспорядки (0–100) — сколько протестов и насколько они сильны.";
            label.MouseFilter = MouseFilterEnum.Pass;
            header.AddChild(label);
            if (player && _countryId != null && p.Party(sec.ArrestTarget) is { } target)
            {
                var id = _countryId;
                var arrest = new Button
                {
                    Text = "Аресты оппозиции", FocusMode = FocusModeEnum.None, Disabled = !sec.CanArrest,
                    TooltipText = sec.CanArrest ? $"Задержать лидеров «{target.Name}»" : $"Снова — не раньше {Fmt.Date(sec.NextArrest ?? default)}",
                };
                arrest.AddThemeColorOverride("font_color", UiTheme.Critical);
                arrest.AddThemeFontSizeOverride("font_size", 13);
                arrest.Pressed += () => ArrestRequested?.Invoke(id);
                header.AddChild(arrest);
            }
        }
        if (p.Security is { } security)
            _unrest.AddChild(ThreatsLine(security));
        if (p.Protests is not { Count: > 0 } protests)
        {
            _unrest.AddChild(Wrapped("Протестов нет. Они начинаются, когда класс радикален, зол и организован; поводом может стать и закон, " +
                                     "который ему сильно не нравится, и раскрытые фальсификации.", 12, UiTheme.TextMuted));
            return;
        }
        foreach (var x in protests.OrderByDescending(x => x.Stage))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(Dot(_data.Classes.Classes[x.Class].Color));
            var stage = UiTheme.Text($"{_data.Classes.Classes[x.Class].Name}: {x.StageName}", 14, UiTheme.SemiBold,
                x.Stage >= 3 ? UiTheme.Critical : x.Stage >= 2 ? UiTheme.Accent : UiTheme.TextPrimary);
            row.AddChild(stage);
            var text = UiTheme.Text($"{x.CauseText} · около {Fmt.People(x.Participants)} человек · {Fmt.Plural(Math.Max(1, x.Months), "месяц", "месяца", "месяцев")}" +
                                    (x.Response != null ? $" · ответ: {ResponseName(x.Response)}" : ""), 13, null, UiTheme.TextSecondary);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.AddChild(text);
            if (player && _countryId != null)
            {
                var id = _countryId;
                var answer = new Button { Text = x.Answered ? "Уже ответили" : "Ответить", Disabled = x.Answered, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 28) };
                answer.AddThemeFontSizeOverride("font_size", 13);
                answer.TooltipText = x.Answered ? "На протест можно ответить раз в месяц" : "Уступить, договориться, промолчать или разогнать";
                answer.Pressed += () => ProtestRequested?.Invoke(id, x.Id);
                row.AddChild(answer);
            }
            _unrest.AddChild(row);
        }
    }

    /// <summary>Угрозы власти за месяц: что и с какой вероятностью может её сменить без выборов.</summary>
    static Control ThreatsLine(SecuritySnapshot security)
    {
        var parts = new List<string>();
        if (security.Threats is { } t)
            foreach (var (name, chance) in new[] { ("вотум недоверия", t.NoConfidence), ("импичмент", t.Impeachment), ("дворцовый переворот", t.PalaceCoup),
                         ("военный переворот", t.MilitaryCoup), ("революция", t.Revolution) })
                if (chance >= 0.001)
                    parts.Add($"{name} — {Fmt.Number(chance * 100, chance < 0.01 ? 1 : 0)}% в месяц");
        var text = parts.Count == 0 ? "Угроз власти сейчас нет: элиты спокойны, армия лояльна, парламент не собирается отправлять правительство в отставку."
            : "Угрозы власти: " + string.Join(", ", parts) + ".";
        if (security.Junta)
            text = "У власти военные. " + (security.HandOver is { } d ? $"Обещают вернуть власть гражданским к {Fmt.Date(d)}. " : "Сроков передачи власти не называют. ") + text;
        var total = security.Threats?.Total ?? 0;
        var label = Wrapped(text, 13, total >= 0.03 ? UiTheme.Critical : total >= 0.005 ? UiTheme.Accent : UiTheme.TextMuted);
        label.TooltipText = "Вотум недоверия — когда у правительства нет большинства или его не любят; импичмент — при скандале или очень низком одобрении; " +
                            "дворцовый переворот — когда недовольны элиты и падает легитимность; военный переворот — когда армия нелояльна; " +
                            "революция — когда идёт восстание, а армия не готова его подавить.";
        label.MouseFilter = MouseFilterEnum.Pass;
        return label;
    }

    static string ResponseName(string response) => response switch
    {
        "concede" => "уступки",
        "negotiate" => "переговоры",
        "disperse" => "разгон",
        _ => "молчание",
    };

    void RenderGroups(CountrySnapshot country, bool player)
    {
        Clear(_groups);
        if (country.Politics?.Groups is not { Count: > 0 } groups)
            return;
        var records = _data.InterestGroups.Groups;
        foreach (var g in groups.OrderByDescending(g => g.Influence))
            if (records.FirstOrDefault(r => r.Id == g.Id) is { } r)
                _groups.AddChild(GroupCard(r, g, player));
    }

    Control GroupCard(InterestGroupRecord r, GroupSnapshot g, bool player)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(320, 0), MouseFilter = MouseFilterEnum.Pass };
        var box = UiTheme.Box(new Color(1, 1, 1, 0.035f), UiTheme.Hairline, 12, 14, 10);
        box.BorderColor = UiTheme.Hex(r.Color);
        (box.BorderWidthLeft, box.BorderWidthTop, box.BorderWidthRight, box.BorderWidthBottom) = (4, 0, 0, 0);
        card.AddThemeStyleboxOverride("panel", box);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        card.AddChild(column);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        column.AddChild(top);
        var name = UiTheme.Text(r.Name, 16, UiTheme.SemiBold);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(name);
        var influence = UiTheme.Text(Fmt.Value("percent", g.Influence * 100), 20, UiTheme.Numbers, UiTheme.TextPrimary);
        influence.TooltipText = "Влияние: доля группы в силе всех групп интересов страны";
        influence.MouseFilter = MouseFilterEnum.Pass;
        top.AddChild(influence);
        column.AddChild(Wrapped(r.Description + " · опирается на " + Base(r), 12, UiTheme.TextMuted));

        var (status, color) = Status(g.Approval);
        var approvalRow = new HBoxContainer();
        approvalRow.AddThemeConstantOverride("separation", 10);
        column.AddChild(approvalRow);
        approvalRow.AddChild(UiTheme.Text($"Одобрение {Fmt.Number(g.Approval, 0)}", 13, UiTheme.Medium, UiTheme.TextSecondary));
        var bar = new ApprovalBar { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        bar.Show(g.Approval, color);
        approvalRow.AddChild(bar);
        approvalRow.AddChild(UiTheme.Text(status, 13, UiTheme.Medium, color));

        var parts = new[] { ("классы", g.FromClasses), ("законы", g.FromLaws), ("бюджет", g.FromBudget), ("власть", g.FromGovernment), ("репрессии", g.FromRepression), ("требования", g.FromDemands) }
            .Where(x => Math.Abs(x.Item2) >= 0.5)
            .Select(x => $"{x.Item1} {Fmt.Signed(x.Item2, 0)}");
        var why = Wrapped("Из чего: " + (parts.Any() ? string.Join(" · ", parts) : "ничего особенного — нейтральна"), 12, UiTheme.TextSecondary);
        why.TooltipText = "Классы — настроение классов группы; законы — принятые после начала игры законы, которые ей нравятся или нет; " +
                          "бюджет — расходы на важные ей статьи (у финансистов — и дефицит); власть — насколько правящие партии близки ей по взглядам; " +
                          "требования — выполненные требования (благодарность со временем проходит).";
        why.MouseFilter = MouseFilterEnum.Pass;
        column.AddChild(why);

        if (g.Pressure > 0.01)
            column.AddChild(Wrapped("Давит на власть: " + r.Pressure + ".", 12, UiTheme.Critical));
        else if (g.Pressure < -0.01)
            column.AddChild(Wrapped("Помогает власти: " + r.Support + ".", 12, Good));

        if (g.Demands.Count == 0)
            column.AddChild(Wrapped("Требований пока нет — новые появятся в январе.", 12, UiTheme.TextMuted));
        foreach (var d in g.Demands)
            column.AddChild(DemandRow(d, player));
        return card;
    }

    Control DemandRow(DemandSnapshot d, bool player)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var text = d.Kind switch
        {
            "spending" => $"{Capital(d.Text)} (сейчас {Fmt.Number(d.Current * 100, 2)}%)",
            "deficit" => $"{Capital(d.Text)} (сейчас {Fmt.Number(d.Current, 1)}%)",
            _ => Capital(d.Text),
        };
        var label = Wrapped("• " + text, 13, UiTheme.TextPrimary);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(label);
        if (player && _countryId != null)
        {
            var id = _countryId;
            var button = new Button { Text = d.Kind == "law" ? "К закону" : "К бюджету", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(92, 26) };
            button.AddThemeFontSizeOverride("font_size", 12);
            button.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            button.Pressed += () =>
            {
                if (d.Kind == "law" && d.Variant != null)
                    LawRequested?.Invoke(id, d.Target, d.Variant);
                else
                    BudgetRequested?.Invoke(id);
            };
            row.AddChild(button);
        }
        return row;
    }

    /// <summary>На кого опирается группа: её классы и отрасли крупного капитала.</summary>
    string Base(InterestGroupRecord r)
    {
        var parts = r.Classes.OrderByDescending(kv => kv.Value)
            .Select(kv => _data.Classes.Classes.FirstOrDefault(c => c.Id == kv.Key))
            .Where(c => c != null)
            .Select(c => (c!.ShortName ?? c.Name).ToLowerInvariant())
            .ToList();
        if (r.Religious)
            parts = ["верующих всех классов"];
        if (r.Industries is { Count: > 0 } industries)
        {
            var names = industries.Select(i => _data.Industries.Industries.FirstOrDefault(x => x.Id == i)?.Name.ToLowerInvariant()).Where(n => n != null).ToList();
            parts.Add("крупный капитал (" + (names.Count > 3 ? "обрабатывающая промышленность и стройка" : string.Join(", ", names)) + ")");
        }
        return string.Join(", ", parts);
    }

    static (string Text, Color Color) Status(double approval) => approval switch
    {
        < 25 => ("враждебна", UiTheme.Critical),
        < 40 => ("недовольна", UiTheme.Critical),
        < 60 => ("нейтральна", UiTheme.TextSecondary),
        < 75 => ("довольна", Good),
        _ => ("лояльна", Good),
    };

    void RenderClasses(CountrySnapshot country)
    {
        Clear(_classes);
        if (country.Classes is not { Count: > 0 } classes)
            return;
        foreach (var (text, align) in new[] { ("класс", false), ("доля", true), ("доход в год", true), ("уровень жизни", true), ("настроение", true),
                     ("радикализм", true), ("влияние", true), ("говорят от его имени", false) })
        {
            var h = UiTheme.Text(text.ToUpperInvariant(), 10, UiTheme.MapCountryFont, UiTheme.TextMuted);
            if (align)
                h.HorizontalAlignment = HorizontalAlignment.Right;
            _classes.AddChild(h);
        }
        var records = _data.Classes.Classes;
        var groups = country.Politics?.Groups ?? [];
        for (var k = 0; k < records.Count && k < classes.Count; k++)
        {
            var r = records[k];
            var c = classes[k];
            var name = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            name.AddThemeConstantOverride("separation", 8);
            name.AddChild(Dot(r.Color));
            var label = UiTheme.Text(r.Name, 13, null, UiTheme.TextSecondary);
            label.TooltipText = "Интересы: " + string.Join(", ", r.Interests).ToLowerInvariant();
            label.MouseFilter = MouseFilterEnum.Pass;
            name.AddChild(label);
            _classes.AddChild(name);
            _classes.AddChild(Number(Fmt.Value("percent", c.Share)));
            _classes.AddChild(Number(Fmt.Value("money", c.Income)));
            _classes.AddChild(Number(Fmt.Number(c.StandardOfLiving, 0)));
            _classes.AddChild(Number(Fmt.Number(c.Satisfaction, 0), ClassTable.MoodColor(c.Satisfaction)));
            _classes.AddChild(Number(Fmt.Number(c.Radicalism, 0), c.Radicalism >= 30 ? UiTheme.Critical : c.Radicalism >= 15 ? UiTheme.Accent : UiTheme.TextPrimary));
            _classes.AddChild(Number(Fmt.Value("percent", c.Influence)));
            var speakers = _data.InterestGroups.Groups
                .Where(g => g.Classes.GetValueOrDefault(r.Id) >= 0.3 || (r.Id == "capitalists" && g.Industries is { Count: > 0 }))
                .Where(g => !g.Religious || groups.FirstOrDefault(x => x.Id == g.Id)?.Influence >= 0.08)
                .Select(g => g.Name);
            _classes.AddChild(UiTheme.Text(string.Join(", ", speakers), 12, null, UiTheme.TextMuted));
        }
    }

    static Label Number(string text, Color? color = null)
    {
        var label = UiTheme.Text(text, 13, UiTheme.Numbers, color ?? UiTheme.TextPrimary);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        return label;
    }

    static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    static Label Wrapped(string text, int size, Color color)
    {
        var label = UiTheme.Text(text, size, null, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    static Control Dot(string color)
    {
        var dot = new Panel { CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        dot.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Hex(color), null, 5, 0, 0));
        return dot;
    }

    static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
            child.QueueFree();
    }

    /// <summary>Шкала одобрения 0–100: зоны давления (до 40) и помощи (от 60), отметка — нынешнее одобрение.</summary>
    sealed partial class ApprovalBar : Control
    {
        double _value;
        Color _color;

        public ApprovalBar()
        {
            CustomMinimumSize = new Vector2(80, 10);
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public void Show(double value, Color color)
        {
            (_value, _color) = (value, color);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var y = Size.Y / 2;
            var w = Size.X;
            DrawLine(new Vector2(0, y), new Vector2(w * 0.4f, y), new Color(0.88f, 0.38f, 0.35f, 0.28f), 6);
            DrawLine(new Vector2(w * 0.4f, y), new Vector2(w * 0.6f, y), new Color(1, 1, 1, 0.1f), 6);
            DrawLine(new Vector2(w * 0.6f, y), new Vector2(w, y), new Color(0.5f, 0.77f, 0.6f, 0.28f), 6);
            var x = (float)Math.Clamp(_value / 100, 0, 1) * w;
            DrawCircle(new Vector2(x, y), 5, _color);
        }
    }
}
