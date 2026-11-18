using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Штаб предвыборной кампании на экране «Политика» (только в стране игрока, за три месяца до выборов): очки штаба,
/// поездки к классам, агитация в СМИ, обещания законов, дебаты лидеров и подсчёт голосов (честно или с фальсификациями).
/// Рядом с каждым действием — оценка, сколько процентных пунктов оно прибавит партии.
/// </summary>
public partial class CampaignPanel : PanelContainer
{
    static readonly Color Good = Color.FromHtml("#7fc49a");
    static readonly string[] FraudNames = ["Честный подсчёт", "Вбросы", "Массовые фальсификации"];

    GameData _data = null!;
    Label _points = null!;
    Label _intro = null!;
    GridContainer _trips = null!;
    HBoxContainer _media = null!;
    VBoxContainer _promises = null!;
    VBoxContainer _debate = null!;
    HBoxContainer _fraud = null!;
    Label _fraudNote = null!;
    string? _countryId;

    public event Action<string, int>? TripRequested;
    public event Action<string>? MediaRequested;
    public event Action<string, string, string>? PledgeRequested;
    public event Action<string, int>? FraudChosen;
    public event Action<string>? DebateRequested;

    public void Setup(GameData data)
    {
        _data = data;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var box = UiTheme.Box(new Color(0.88f, 0.68f, 0.32f, 0.05f), new Color(0.88f, 0.68f, 0.32f, 0.35f), 12, 16, 14);
        AddThemeStyleboxOverride("panel", box);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        column.AddChild(header);
        var caption = UiTheme.Text("ПРЕДВЫБОРНАЯ КАМПАНИЯ", 11, UiTheme.MapCountryFont, UiTheme.Accent);
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(caption);
        _points = UiTheme.Text("", 15, UiTheme.SemiBold, UiTheme.TextPrimary);
        header.AddChild(_points);
        _intro = Wrapped("", 13, UiTheme.TextSecondary);
        column.AddChild(_intro);

        column.AddChild(Caption("ПОЕЗДКИ И ВСТРЕЧИ С КЛАССАМИ"));
        _trips = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _trips.AddThemeConstantOverride("h_separation", 18);
        _trips.AddThemeConstantOverride("v_separation", 4);
        column.AddChild(_trips);
        _media = new HBoxContainer();
        _media.AddThemeConstantOverride("separation", 10);
        column.AddChild(_media);

        column.AddChild(Caption("ОБЕЩАНИЯ ИЗБИРАТЕЛЯМ"));
        column.AddChild(Wrapped("Обещание привлекает тех, кому закон нравится, и отталкивает остальных. Если победите, закон нужно принять за два года — " +
                                "иначе партия и вы потеряете доверие. Каждое следующее обещание дороже.", 12, UiTheme.TextMuted));
        _promises = new VBoxContainer();
        _promises.AddThemeConstantOverride("separation", 4);
        column.AddChild(_promises);

        _debate = new VBoxContainer();
        _debate.AddThemeConstantOverride("separation", 4);
        column.AddChild(_debate);

        column.AddChild(Caption("ПОДСЧЁТ ГОЛОСОВ"));
        _fraud = new HBoxContainer();
        _fraud.AddThemeConstantOverride("separation", 8);
        column.AddChild(_fraud);
        _fraudNote = Wrapped("", 12, UiTheme.TextMuted);
        column.AddChild(_fraudNote);
    }

    public void Show(string countryId, PoliticsSnapshot p, CampaignSnapshot c)
    {
        _countryId = countryId;
        var whole = c.Points == Math.Round(c.Points);
        _points.Text = $"Очки штаба: {Fmt.Number(c.Points, whole ? 0 : 1)}";
        _points.TooltipText = $"Каждый месяц штаб получает {Fmt.Number(c.PointsPerMonth, 0)} (больше у организованных партий); копить можно не больше чем на три месяца.";
        _points.MouseFilter = MouseFilterEnum.Pass;
        _intro.Text = $"Выборы {Fmt.Date(c.Election)}. Каждый месяц штаб получает {Fmt.Plural((long)c.PointsPerMonth, "очко", "очка", "очков")}. " +
                      "Симпатии, завоёванные кампанией, со временем остывают: поездки накануне выборов действуют сильнее. Справа — сколько, " +
                      "по оценке штаба, действие прибавит вашей партии.";
        RenderTrips(c);
        RenderPromises(c);
        RenderDebate(c);
        RenderFraud(c);
    }

    void RenderTrips(CampaignSnapshot c)
    {
        Clear(_trips);
        var classes = _data.Classes.Classes;
        for (var k = 0; k < classes.Count && k < c.TripGain.Count; k++)
        {
            var index = k;
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(Dot(classes[k].Color));
            var name = UiTheme.Text(classes[k].ShortName ?? classes[k].Name, 13, null, UiTheme.TextSecondary);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.ClipText = true;
            name.TooltipText = classes[k].Name;
            name.MouseFilter = MouseFilterEnum.Pass;
            row.AddChild(name);
            var share = UiTheme.Text($"за вас {Fmt.Number(c.ClassShare[k] * 100, 0)}%", 12, UiTheme.Numbers, UiTheme.TextMuted);
            share.CustomMinimumSize = new Vector2(74, 0);
            share.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(share);
            row.AddChild(Gain(c.TripGain[k]));
            var go = SmallButton("Поехать", c.Points >= c.TripCost - 1e-9 && c.TripGain[k] > 0.0001);
            go.TooltipText = $"Поездка, митинги и встречи: {Fmt.Plural((long)c.TripCost, "очко", "очка", "очков")} штаба";
            go.Pressed += () => Emit(id => TripRequested?.Invoke(id, index));
            row.AddChild(go);
            _trips.AddChild(row);
        }
        Clear(_media);
        var text = UiTheme.Text($"Агитация в СМИ — ролики, интервью, выступления: всем классам понемногу · {Fmt.Plural((long)c.MediaCost, "очко", "очка", "очков")}",
            13, null, UiTheme.TextSecondary);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _media.AddChild(text);
        _media.AddChild(Gain(c.MediaGain));
        var media = SmallButton("Запустить", c.Points >= c.MediaCost - 1e-9 && c.MediaGain > 0.0001);
        media.Pressed += () => Emit(id => MediaRequested?.Invoke(id));
        _media.AddChild(media);
    }

    void RenderPromises(CampaignSnapshot c)
    {
        Clear(_promises);
        if (c.Options.Count == 0)
        {
            _promises.AddChild(Wrapped("Обещать больше нечего: всё, что могло бы привлечь избирателей, уже обещано или принято.", 13, UiTheme.TextMuted));
            return;
        }
        foreach (var option in c.Options)
        {
            var law = _data.Law(option.Law);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var text = UiTheme.Text($"«{law.Name}: {law.Variant(option.Variant).Name.ToLowerInvariant()}»", 13, null, UiTheme.TextPrimary);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.TooltipText = law.Variant(option.Variant).Description;
            text.MouseFilter = MouseFilterEnum.Pass;
            row.AddChild(text);
            row.AddChild(Gain(option.Gain));
            var pledge = SmallButton($"Пообещать · {Fmt.Number(c.PromiseCost, 0)}", c.Points >= c.PromiseCost - 1e-9);
            pledge.Pressed += () => Emit(id => PledgeRequested?.Invoke(id, option.Law, option.Variant));
            row.AddChild(pledge);
            _promises.AddChild(row);
        }
    }

    void RenderDebate(CampaignSnapshot c)
    {
        Clear(_debate);
        _debate.Visible = c.Debate != null;
        if (c.Debate is not { } d)
            return;
        _debate.AddChild(Caption("ДЕБАТЫ ЛИДЕРОВ"));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _debate.AddChild(row);
        var text = d.Answer is { } answer
            ? $"Дебаты прошли. Тема — {d.Topic.ToLowerInvariant()}; ваш ответ: «{d.Answers[answer + 1]}»."
            : $"Тема — {d.Topic.ToLowerInvariant()}: {d.Question}";
        var label = Wrapped(text, 13, d.Answer == null ? UiTheme.TextPrimary : UiTheme.TextSecondary);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(label);
        if (d.Answer == null)
        {
            var go = SmallButton("Выйти на дебаты", true);
            go.AddThemeColorOverride("font_color", UiTheme.Accent);
            go.Pressed += () => Emit(id => DebateRequested?.Invoke(id));
            row.AddChild(go);
        }
    }

    void RenderFraud(CampaignSnapshot c)
    {
        Clear(_fraud);
        for (var level = 0; level < c.FraudShare.Count && level < FraudNames.Length; level++)
        {
            var index = level;
            var text = level == 0 ? FraudNames[0] : $"{FraudNames[level]}: +{Fmt.Number(c.FraudShare[level] * 100, 0)}%, риск {Fmt.Number(c.FraudRisk[level] * 100, 0)}%";
            var button = new Button { Text = text, ToggleMode = true, ButtonPressed = c.Fraud == level, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) };
            button.AddThemeFontSizeOverride("font_size", 13);
            if (level > 0)
                button.AddThemeColorOverride("font_pressed_color", UiTheme.Critical);
            button.TooltipText = level == 0
                ? "Голоса считают честно"
                : $"К доле вашей партии прибавится около {Fmt.Number(c.FraudShare[level] * 100, 0)}% голосов. Вероятность разоблачения — " +
                  $"{Fmt.Number(c.FraudRisk[level] * 100, 0)}%: она выше при свободной прессе и сильной оппозиции.";
            button.Pressed += () => Emit(id => FraudChosen?.Invoke(id, index));
            _fraud.AddChild(button);
        }
        _fraudNote.Text = c.Fraud > 0
            ? "Если фальсификации раскроют — скандал: доверие к власти рухнет, популярность главы упадёт, люди станут радикальнее, а опросы правящих партий просядут."
            : "Фальсификации прибавляют голоса, но их могут раскрыть журналисты и наблюдатели. Решение можно поменять до дня выборов.";
        _fraudNote.AddThemeColorOverride("font_color", c.Fraud > 0 ? UiTheme.Critical : UiTheme.TextMuted);
    }

    void Emit(Action<string> action)
    {
        if (_countryId != null)
            action(_countryId);
    }

    static Label Gain(double gain)
    {
        var label = UiTheme.Text(Math.Abs(gain) < 0.0005 ? "—" : $"≈{Fmt.Signed(gain * 100, 1)} п. п.", 12, UiTheme.Numbers,
            gain > 0.0005 ? Good : gain < -0.0005 ? UiTheme.Critical : UiTheme.TextMuted);
        label.CustomMinimumSize = new Vector2(84, 0);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.TooltipText = "Оценка штаба: на сколько процентных пунктов вырастет доля партии в опросах";
        label.MouseFilter = MouseFilterEnum.Pass;
        return label;
    }

    static Button SmallButton(string text, bool enabled)
    {
        var button = new Button { Text = text, Disabled = !enabled, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(96, 28) };
        button.AddThemeFontSizeOverride("font_size", 13);
        return button;
    }

    static Label Caption(string text) => UiTheme.Text(text, 10, UiTheme.MapCountryFont, UiTheme.TextMuted);

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
}
