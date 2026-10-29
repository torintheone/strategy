using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Экран «Бюджет и налоги» страны игрока: ползунки налогов (множитель к нынешней ставке) и расходов (доля ВВП),
/// итог — доходы, расходы, проценты и сальдо, прогноз для классов и голосования. Осенью редактируется проект
/// на следующий год, в остальное время — поправки к действующему бюджету. При подчинённом центробанке здесь же
/// задаётся ключевая ставка.
/// </summary>
public partial class BudgetScreen : GlassPanel
{
    GameData _data = null!;
    Label _title = null!;
    Label _status = null!;
    readonly List<(HSlider Slider, Label Value)> _taxes = new();
    readonly List<(HSlider Slider, Label Value)> _spending = new();
    Label _summary = null!;
    HFlowContainer _classes = null!;
    Label _forecast = null!;
    Button _submit = null!;
    Button _discard = null!;
    Control _rateBox = null!;
    HSlider _rate = null!;
    Label _rateValue = null!;
    Label _notPlayer = null!;
    Control _content = null!;
    BudgetSnapshot? _shown;
    int _syncedVersion = -1;
    double _pending = -1;
    double _ratePending = -1;

    public event Action? Closed;
    public event Action<string, double[], double[]>? Edited;
    public event Action<string>? AmendmentSubmitted;
    public event Action<string>? AmendmentDiscarded;
    public event Action<string, double?>? RateChosen;

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
        column.AddThemeConstantOverride("separation", 10);
        Body.AddChild(column);
        var header = new HBoxContainer();
        column.AddChild(header);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 2);
        header.AddChild(titles);
        _title = UiTheme.Text("Бюджет и налоги", 26, UiTheme.SerifBold);
        titles.AddChild(_title);
        _status = UiTheme.Text("", 14, null, UiTheme.AccentLight);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titles.AddChild(_status);
        var close = new Button { Text = "×", Flat = true, TooltipText = "Закрыть (Esc)", FocusMode = FocusModeEnum.None };
        close.AddThemeFontSizeOverride("font_size", 22);
        close.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        close.Pressed += () => Closed?.Invoke();
        header.AddChild(close);

        _notPlayer = UiTheme.Text("Бюджет можно менять только в своей стране. Выберите страну на карте и нажмите «Играть за эту страну».", 15, null, UiTheme.TextSecondary);
        _notPlayer.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_notPlayer);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        _content = scroll;
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(body);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 28);
        body.AddChild(columns);
        columns.AddChild(Column("ДОХОДЫ: НАЛОГОВЫЕ СТАВКИ", _data.Budget.Revenues.Select(r => r.Name), _taxes));
        columns.AddChild(Column("РАСХОДЫ, % ВВП", _data.Budget.Spending.Select(s => s.Name), _spending));

        body.AddChild(new HSeparator());
        _summary = UiTheme.Text("", 15, UiTheme.Numbers, UiTheme.TextPrimary);
        _summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_summary);
        body.AddChild(UiTheme.Text("КАК ОТНЕСУТСЯ КЛАССЫ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        _classes = new HFlowContainer();
        _classes.AddThemeConstantOverride("h_separation", 6);
        _classes.AddThemeConstantOverride("v_separation", 6);
        body.AddChild(_classes);
        _forecast = UiTheme.Text("", 14, null, UiTheme.TextPrimary);
        _forecast.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_forecast);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        body.AddChild(actions);
        _submit = new Button { Text = "Внести поправки в парламент", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(260, 38) };
        _submit.AddThemeColorOverride("font_color", UiTheme.Ocean);
        _submit.AddThemeColorOverride("font_hover_color", UiTheme.Ocean);
        _submit.AddThemeFontOverride("font", UiTheme.SemiBold);
        _submit.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.Accent, null, 10, 14, 8));
        _submit.AddThemeStyleboxOverride("hover", UiTheme.Box(UiTheme.AccentLight, null, 10, 14, 8));
        _submit.Pressed += () =>
        {
            if (_shown != null)
                AmendmentSubmitted?.Invoke(_shown.Country);
        };
        actions.AddChild(_submit);
        _discard = new Button { Text = "Отменить изменения", FocusMode = FocusModeEnum.None };
        _discard.Pressed += () =>
        {
            if (_shown != null)
            {
                AmendmentDiscarded?.Invoke(_shown.Country);
                _syncedVersion = -1;
            }
        };
        actions.AddChild(_discard);

        body.AddChild(new HSeparator());
        var rateBox = new VBoxContainer();
        rateBox.AddThemeConstantOverride("separation", 4);
        _rateBox = rateBox;
        body.AddChild(rateBox);
        rateBox.AddChild(UiTheme.Text("КЛЮЧЕВАЯ СТАВКА", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        rateBox.AddChild(UiTheme.Text("Центробанк подчинён правительству: ставку задаёте вы. Низкая ставка разгоняет экономику и инфляцию, высокая — гасит обе.", 13, null, UiTheme.TextSecondary));
        var rateRow = new HBoxContainer();
        rateRow.AddThemeConstantOverride("separation", 12);
        rateBox.AddChild(rateRow);
        _rate = new HSlider { MinValue = 0, MaxValue = 40, Step = 0.25, CustomMinimumSize = new Vector2(360, 24), FocusMode = FocusModeEnum.None };
        _rate.ValueChanged += _ => _ratePending = 0.4;
        rateRow.AddChild(_rate);
        _rateValue = UiTheme.Text("", 15, UiTheme.Numbers);
        rateRow.AddChild(_rateValue);
        var auto = new Button { Text = "Пусть решает центробанк", FocusMode = FocusModeEnum.None };
        auto.Pressed += () =>
        {
            if (_shown != null)
                RateChosen?.Invoke(_shown.Country, null);
        };
        rateRow.AddChild(auto);
    }

    Control Column(string caption, IEnumerable<string> names, List<(HSlider, Label)> rows)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 2);
        column.AddChild(UiTheme.Text(caption, 11, UiTheme.MapCountryFont, UiTheme.Accent));
        foreach (var name in names)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var label = UiTheme.Text(name, 13, null, UiTheme.TextSecondary);
            label.CustomMinimumSize = new Vector2(220, 0);
            label.ClipText = true;
            row.AddChild(label);
            var slider = new HSlider { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(160, 22), FocusMode = FocusModeEnum.None };
            slider.ValueChanged += _ => _pending = 0.35;
            row.AddChild(slider);
            var value = UiTheme.Text("", 13, UiTheme.Numbers, UiTheme.TextPrimary);
            value.CustomMinimumSize = new Vector2(150, 0);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(value);
            column.AddChild(row);
            rows.Add((slider, value));
        }
        return column;
    }

    public void Open(WorldSnapshot snapshot)
    {
        var appearing = !Visible;
        Visible = true;
        _syncedVersion = -1;
        Refresh(snapshot);
        if (appearing)
            AnimateIn(new Vector2(0, 0.1f), 0.2f, 0.985f);
    }

    public override void _Process(double delta)
    {
        // Ползунки отправляют изменения с небольшой задержкой, чтобы не считать прогноз на каждый пиксель.
        if (_pending > 0 && (_pending -= delta) <= 0 && _shown != null)
        {
            Edited?.Invoke(_shown.Country, _spending.Select(r => r.Slider.Value / 100).ToArray(), _taxes.Select(r => r.Slider.Value / 100).ToArray());
        }
        if (_ratePending > 0 && (_ratePending -= delta) <= 0 && _shown != null)
            RateChosen?.Invoke(_shown.Country, _rate.Value / 100);
        UpdateLabels();
    }

    public void Refresh(WorldSnapshot snapshot)
    {
        var b = snapshot.Budget;
        _notPlayer.Visible = b == null;
        _content.Visible = b != null;
        if (b == null)
        {
            _status.Text = "";
            return;
        }
        _shown = b;
        var name = _data.Countries.Countries.First(c => c.Id == b.Country).Name;
        _title.Text = "Бюджет и налоги: " + name;
        _status.Text = Status(b);

        // Ползунки берут значения из модели, пока игрок их не двигает.
        if (b.Version != _syncedVersion && _pending <= 0)
        {
            _syncedVersion = b.Version;
            var cycle = _data.BudgetCycleBalance;
            for (var k = 0; k < _taxes.Count; k++)
            {
                var s = _taxes[k].Slider;
                s.MinValue = cycle.TaxMin * 100;
                s.MaxValue = cycle.TaxMax * 100;
                s.Step = 1;
                s.SetValueNoSignal(b.EditTaxes[k] * 100);
            }
            for (var k = 0; k < _spending.Count; k++)
            {
                var s = _spending[k].Slider;
                var baseValue = b.DraftYear > 0 ? b.EditSpending[k] : b.SpendingPlan[k];
                s.MinValue = 0;
                s.MaxValue = Math.Max(cycle.SpendingMax * Math.Max(baseValue, b.SpendingPlan[k]), 0.5) * 100;
                s.Step = 0.01;
                s.SetValueNoSignal(b.EditSpending[k] * 100);
            }
        }

        foreach (var child in _classes.GetChildren())
            child.QueueFree();
        if (b.Forecast is { } f)
        {
            var classes = _data.Classes.Classes;
            for (var k = 0; k < classes.Count && k < f.Attitudes.Length; k++)
            {
                if (Math.Abs(f.Attitudes[k]) < 0.05 && Math.Abs(f.IncomeChange[k]) < 0.0005)
                    continue;
                var color = f.Attitudes[k] >= 0 ? Color.FromHtml("#7fc49a") : UiTheme.Critical;
                var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
                chip.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(color, 0.14f), new Color(color, 0.4f), 8, 8, 3));
                chip.AddChild(UiTheme.Text($"{classes[k].ShortName ?? classes[k].Name} {Fmt.Signed(f.Attitudes[k], 1)}", 12, UiTheme.Medium, color));
                chip.TooltipText = $"Доход класса изменится на {Fmt.Value("percent_signed", f.IncomeChange[k] * 100)}";
                _classes.AddChild(chip);
            }
            if (_classes.GetChildCount() == 0)
                _classes.AddChild(UiTheme.Text("Изменений нет — классы равнодушны.", 13, null, UiTheme.TextMuted));
            var v = f.Vote;
            var vote = v.Decree ? "Парламента нет: бюджет утверждается указом."
                : $"Прогноз голосования: за {v.Yes}, против {v.No}, нужно {v.Required} — {(v.Passed ? "скорее всего пройдёт" : "скорее всего не пройдёт")}.";
            _forecast.Text = $"Одобрение в обществе: {Fmt.Value("percent", f.Support * 100)}. {vote}";
        }
        else
        {
            _classes.AddChild(UiTheme.Text("Передвиньте ползунки — здесь появится прогноз.", 13, null, UiTheme.TextMuted));
            _forecast.Text = "";
        }

        var editingAmendment = b.DraftYear == 0;
        _submit.Visible = editingAmendment && b.Amending && b.AmendVote == null;
        _discard.Visible = editingAmendment && b.Amending && b.AmendVote == null;
        _rateBox.Visible = b.CanSetRate;
        if (b.CanSetRate && _ratePending <= 0)
            _rate.SetValueNoSignal((b.ManualRate ?? b.PolicyRate) * 100);
        UpdateLabels();
    }

    string Status(BudgetSnapshot b)
    {
        var text = b.Stage switch
        {
            "draft" => $"Проект бюджета на {b.DraftYear} год. Парламент проголосует {Fmt.Date(b.NextVote!.Value)} — до этого проект можно изменить.",
            "rejected" => $"Парламент отклонил проект на {b.DraftYear} год. Повторное голосование {Fmt.Date(b.NextVote!.Value)}: измените проект, чтобы найти поддержку.",
            "passed" => $"Бюджет на {b.DraftYear} год принят и вступит в силу 1 января.",
            "failed" => $"Бюджет на {b.DraftYear} год не принят: с 1 января страна будет жить по прошлогоднему бюджету.",
            _ => b.AmendVote is { } when
                ? $"Поправки внесены в парламент, голосование {Fmt.Date(when)}."
                : "Действующий бюджет. Изменения — это поправки: их нужно внести в парламент. Проект на следующий год появится в сентябре.",
        };
        return b.LastResult is { } last ? text + "\nПоследнее решение: " + last : text;
    }

    void UpdateLabels()
    {
        if (_shown is not { } b)
            return;
        double revenue = 0, spending = 0;
        for (var k = 0; k < _taxes.Count; k++)
        {
            var multiplier = _taxes[k].Slider.Value / 100;
            var share = b.RevenueShare[k] * multiplier / Math.Max(1e-9, b.TaxPolicy[k]);
            revenue += share;
            _taxes[k].Value.Text = $"{Fmt.Value("percent", share * 100)} ВВП · ×{Fmt.Number(multiplier, 2)}";
        }
        for (var k = 0; k < _spending.Count; k++)
        {
            var share = b.SpendingShare[k] + (_spending[k].Slider.Value / 100 - b.SpendingPlan[k]);
            spending += Math.Max(0, share);
            var delta = _spending[k].Slider.Value / 100 - b.SpendingPlan[k];
            _spending[k].Value.Text = Fmt.Value("percent", Math.Max(0, share) * 100) + " ВВП" +
                                      (Math.Abs(delta) >= 0.0005 ? $" ({Fmt.Signed(delta * 100, 1)})" : "");
        }
        var balance = revenue - spending - b.Interest;
        _summary.Text = $"Доходы {Fmt.Value("percent", revenue * 100)} ВВП · расходы {Fmt.Value("percent", spending * 100)} · проценты по долгу " +
                        $"{Fmt.Value("percent", b.Interest * 100)} · сальдо {Fmt.Value("percent_signed", balance * 100)} ВВП (сейчас {Fmt.Value("percent_signed", b.Balance * 100)})";
        _summary.AddThemeColorOverride("font_color", balance < -0.05 ? UiTheme.Critical : UiTheme.TextPrimary);
        if (b.CanSetRate)
            _rateValue.Text = $"{Fmt.Value("percent", _rate.Value)} (сейчас {Fmt.Value("percent", b.PolicyRate * 100)})";
    }
}
