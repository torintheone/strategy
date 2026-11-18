using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Экран «Наука»: очки исследований и из чего они, распределение очков по восьми ветвям (в своей стране — кнопками),
/// приоритетная технология и дерево технологий: что освоено, что изучается, что можно взять в приоритет, что пока не
/// открыл никто в мире.
/// </summary>
public partial class ScienceScreen : GlassPanel
{
    static readonly Color Done = Color.FromHtml("#7fc49a");

    GameData _data = null!;
    Label _title = null!;
    Label _subtitle = null!;
    Label _summary = null!;
    VBoxContainer _branches = null!;
    HBoxContainer _priority = null!;
    GridContainer _tree = null!;
    string? _countryId;
    string? _shown;

    public event Action? Closed;
    /// <summary>Страна, ветвь, на сколько изменить её долю.</summary>
    public event Action<string, string, double>? AllocationChanged;
    /// <summary>Страна, технология (null — снять приоритет).</summary>
    public event Action<string, string?>? PriorityChosen;

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

        _summary = Wrapped("", 14, UiTheme.TextSecondary);
        content.AddChild(_summary);
        content.AddChild(UiTheme.Text("РАСПРЕДЕЛЕНИЕ ОЧКОВ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        _branches = new VBoxContainer();
        _branches.AddThemeConstantOverride("separation", 4);
        content.AddChild(_branches);
        _priority = new HBoxContainer();
        _priority.AddThemeConstantOverride("separation", 10);
        content.AddChild(_priority);

        content.AddChild(UiTheme.Text("ДЕРЕВО ТЕХНОЛОГИЙ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        content.AddChild(Wrapped("Технологию, которую уже освоила большая часть мира, осваивать дешевле; открыть новое — дорого, это под силу " +
                                 "только странам с большими деньгами на науку. Производительность и продолжительность жизни растут в сравнении " +
                                 "с миром: преимущество тает, когда технологию осваивают другие.", 12, UiTheme.TextMuted));
        _tree = new GridContainer { Columns = _data.Science.Branches.Count, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _tree.AddThemeConstantOverride("h_separation", 8);
        _tree.AddThemeConstantOverride("v_separation", 8);
        content.AddChild(_tree);
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
        if (_countryId == null || !snapshot.Countries.TryGetValue(_countryId, out var country) || country.Science is not { } science)
            return;
        var player = snapshot.PlayerCountry == _countryId;
        // Перерисовываем, только когда что-то изменилось: месяц, распределение, приоритет, освоенное.
        var signature = $"{snapshot.Date:yyyy-MM}|{player}|{science.Priority}|{string.Join(",", science.Allocation.Select(a => a.ToString("F3")))}|{science.Researched.Count}";
        if (signature == _shown)
            return;
        _shown = signature;
        var record = _data.Countries.Countries.First(c => c.Id == _countryId);
        _title.Text = "Наука: " + record.Name;
        _subtitle.Text = player
            ? "Распределяйте очки по ветвям кнопками − и +; нажмите на технологию в дереве, чтобы учёные занялись ею в первую очередь"
            : "вы можете только смотреть: это не ваша страна";
        var total = _data.Science.Technologies.Count;
        _summary.Text = $"Очки исследований — {Fmt.Number(science.Points, 2)}% ВВП в год: государство тратит на науку {Fmt.Number(science.Government, 2)}% ВВП, " +
                        $"бизнес — {Fmt.Number(science.Private, 2)}%, исследователи работают на {Fmt.Value("percent", science.Efficiency * 100)} от прежнего " +
                        $"(специалисты, качество образования, свободы). Утечка мозгов — {Fmt.Value("percent", science.Drain * 100)} в год. " +
                        $"Освоено {science.Researched.Count} технологий из {total}.";
        RenderBranches(science, player);
        RenderPriority(science, player);
        RenderTree(science, snapshot, player);
    }

    void RenderBranches(ScienceSnapshot science, bool player)
    {
        Clear(_branches);
        var branches = _data.Science.Branches;
        for (var k = 0; k < branches.Count; k++)
        {
            var branch = branches[k];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var name = UiTheme.Text(branch.Name, 14, null, UiTheme.TextPrimary);
            name.CustomMinimumSize = new Vector2(320, 0);
            row.AddChild(name);
            if (player)
                row.AddChild(Step("−", _countryId!, branch.Id, -0.05));
            var share = UiTheme.Text(Fmt.Value("percent", science.Allocation[k] * 100), 14, UiTheme.Numbers, UiTheme.TextPrimary);
            share.CustomMinimumSize = new Vector2(64, 0);
            share.HorizontalAlignment = HorizontalAlignment.Center;
            row.AddChild(share);
            if (player)
                row.AddChild(Step("+", _countryId!, branch.Id, 0.05));
            var techs = _data.Science.Technologies.Where(t => t.Branch == branch.Id).OrderBy(t => t.Tier).ToList();
            var done = techs.Count(t => science.Researched.Contains(t.Id));
            var target = techs.FirstOrDefault(t => !science.Researched.Contains(t.Id) && science.Progress?.ContainsKey(t.Id) == true
                                                   && science.YearsLeft?.ContainsKey(t.Id) == true);
            var text = $"освоено {done} из {techs.Count}";
            if (target != null && science.Progress![target.Id] is var progress && science.YearsLeft![target.Id] is var years)
                text += $" · изучается «{target.Name}»: {Fmt.Value("percent", progress * 100)}, {Years(years)}";
            else if (done == techs.Count)
                text += " · всё освоено";
            else if (techs.FirstOrDefault(t => !science.Researched.Contains(t.Id)) is { } next
                     && (next.Requires ?? []).Where(r => !science.Researched.Contains(r)).ToList() is { Count: > 0 } missing)
                text += " · «" + next.Name + "» ждёт технологии " + string.Join(", ", missing.Select(r => "«" + _data.Science.Technologies.First(t => t.Id == r).Name + "»"))
                        + " — очки ветви идут в другие";
            var info = UiTheme.Text(text, 13, null, UiTheme.TextSecondary);
            info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(info);
            _branches.AddChild(row);
        }
    }

    void RenderPriority(ScienceSnapshot science, bool player)
    {
        Clear(_priority);
        if (science.Priority is { } id && _data.Science.Technologies.FirstOrDefault(t => t.Id == id) is { } tech)
        {
            var progress = science.Progress?.GetValueOrDefault(id) ?? 0;
            var years = science.YearsLeft?.GetValueOrDefault(id);
            var label = Wrapped($"Приоритет — «{tech.Name}»: на неё идёт половина всех очков. Готово {Fmt.Value("percent", progress * 100)}" +
                                (years is { } y ? $", {Years(y)}." : "."), 14, UiTheme.Accent);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _priority.AddChild(label);
            if (player)
            {
                var clear = new Button { Text = "Снять приоритет", FocusMode = FocusModeEnum.None };
                clear.AddThemeFontSizeOverride("font_size", 13);
                clear.Pressed += () => PriorityChosen?.Invoke(_countryId!, null);
                _priority.AddChild(clear);
            }
            return;
        }
        if (player)
            _priority.AddChild(Wrapped("Приоритета нет: нажмите на доступную технологию в дереве, и учёные займутся ею в первую очередь.", 13, UiTheme.TextMuted));
    }

    void RenderTree(ScienceSnapshot science, WorldSnapshot snapshot, bool player)
    {
        Clear(_tree);
        foreach (var branch in _data.Science.Branches)
        {
            var head = Wrapped(branch.Name, 12, UiTheme.TextSecondary);
            head.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            head.CustomMinimumSize = new Vector2(150, 34);
            _tree.AddChild(head);
        }
        var tiers = _data.Science.Technologies.Max(t => t.Tier);
        for (var tier = 1; tier <= tiers; tier++)
            foreach (var branch in _data.Science.Branches)
            {
                var tech = _data.Science.Technologies.FirstOrDefault(t => t.Branch == branch.Id && t.Tier == tier);
                _tree.AddChild(tech != null ? Cell(tech, science, snapshot, player) : new Control());
            }
    }

    Control Cell(TechnologyRecord tech, ScienceSnapshot science, WorldSnapshot snapshot, bool player)
    {
        var done = science.Researched.Contains(tech.Id);
        var open = !done && science.Progress?.ContainsKey(tech.Id) == true;
        var adoption = snapshot.Adoption.GetValueOrDefault(tech.Id);
        string status;
        Color fill, border, text;
        if (done)
        {
            status = "освоена";
            (fill, border, text) = (new Color(0.5f, 0.77f, 0.6f, 0.12f), new Color(0.5f, 0.77f, 0.6f, 0.45f), UiTheme.TextPrimary);
        }
        else if (open)
        {
            var progress = science.Progress![tech.Id];
            status = (tech.Id == science.Priority ? "приоритет · " : "") + (progress > 0.005 ? $"готово {Fmt.Value("percent", progress * 100)}" : "можно изучать")
                     + (adoption > 0 ? $" · у {Fmt.Value("percent", adoption * 100)} мира" : " · новое для мира");
            (fill, border, text) = (new Color(1, 1, 1, 0.05f), tech.Id == science.Priority ? UiTheme.Accent : UiTheme.Hairline, UiTheme.TextPrimary);
        }
        else
        {
            status = adoption > 0 ? $"у {Fmt.Value("percent", adoption * 100)} мира" : "не открыта никем";
            (fill, border, text) = (new Color(1, 1, 1, 0.015f), new Color(1, 1, 1, 0.05f), UiTheme.TextMuted);
        }
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(fill, border, 8, 10, 6));
        card.TooltipText = tech.Description + (player && open && tech.Id != science.Priority ? "\nНажмите, чтобы сделать приоритетом." : "");
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 2);
        card.AddChild(column);
        var name = Wrapped(tech.Name, 13, text);
        name.MouseFilter = MouseFilterEnum.Ignore;
        column.AddChild(name);
        var line = Wrapped(status, 11, done ? Done : UiTheme.TextMuted);
        line.MouseFilter = MouseFilterEnum.Ignore;
        column.AddChild(line);
        if (player && open && tech.Id != science.Priority)
        {
            card.MouseDefaultCursorShape = CursorShape.PointingHand;
            card.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    PriorityChosen?.Invoke(_countryId!, tech.Id);
            };
        }
        return card;
    }

    Button Step(string text, string country, string branch, double delta)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 26) };
        button.AddThemeFontSizeOverride("font_size", 14);
        button.Pressed += () => AllocationChanged?.Invoke(country, branch, delta);
        return button;
    }

    static string Years(double years) => years < 1 ? "меньше года" : years > 60 ? "больше 60 лет" : "≈ " + Fmt.Plural((long)Math.Round(years), "год", "года", "лет");

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
