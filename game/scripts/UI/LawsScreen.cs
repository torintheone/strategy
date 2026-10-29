using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Экран «Законы»: слева каталог по 11 группам, справа выбранный закон — действующий вариант, все варианты,
/// что изменится и как к этому отнесутся классы, прогноз голосования. В своей стране игрок поручает ведомству
/// подготовить закон; внизу — законопроекты в работе и последние решения.
/// </summary>
public partial class LawsScreen : GlassPanel
{
    GameData _data = null!;
    Dictionary<string, PostRecord> _posts = null!;
    Label _title = null!;
    Label _subtitle = null!;
    readonly Dictionary<string, (Button Button, Label Status)> _rows = new();
    Label _lawTitle = null!;
    Label _lawMeta = null!;
    Label _lawDescription = null!;
    VBoxContainer _variants = null!;
    VBoxContainer _changes = null!;
    Label _forecast = null!;
    Button _propose = null!;
    Button _withdraw = null!;
    VBoxContainer _bills = null!;
    string? _countryId;
    string _lawId = "";
    string? _variantId;
    CountrySnapshot? _shown;
    bool _shownPlayer;

    public event Action? Closed;
    public event Action<string, string, string>? Proposed;
    public event Action<string, string>? Withdrawn;

    public string? CountryId => _countryId;

    public void Setup(GameData data)
    {
        _data = data;
        _posts = data.Government.Posts.ToDictionary(p => p.Id);
        _lawId = data.Laws.Laws[0].Id;
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
        split.AddChild(BuildCatalog());
        split.AddChild(BuildDetail());
        SelectLaw(_lawId);
    }

    Control BuildCatalog()
    {
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(380, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(list);
        foreach (var group in _data.Laws.Groups)
        {
            var caption = UiTheme.Text(group.Name.ToUpperInvariant(), 11, UiTheme.MapCountryFont, UiTheme.Accent);
            caption.CustomMinimumSize = new Vector2(0, 26);
            caption.VerticalAlignment = VerticalAlignment.Bottom;
            list.AddChild(caption);
            foreach (var law in _data.Laws.Laws.Where(l => l.Group == group.Id))
            {
                var row = new HBoxContainer();
                var button = new Button
                {
                    Text = law.Name,
                    Flat = true,
                    ToggleMode = true,
                    FocusMode = FocusModeEnum.None,
                    Alignment = HorizontalAlignment.Left,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    ClipText = true,
                };
                button.AddThemeFontSizeOverride("font_size", 14);
                var id = law.Id;
                button.Pressed += () => SelectLaw(id);
                row.AddChild(button);
                var status = UiTheme.Text("", 11, UiTheme.Medium, UiTheme.Accent);
                status.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(status);
                list.AddChild(row);
                _rows[law.Id] = (button, status);
            }
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
        _lawTitle = UiTheme.Text("", 22, UiTheme.SerifBold);
        column.AddChild(_lawTitle);
        _lawMeta = UiTheme.Text("", 13, null, UiTheme.TextMuted);
        _lawMeta.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_lawMeta);
        _lawDescription = UiTheme.Text("", 15, null, UiTheme.TextSecondary);
        _lawDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_lawDescription);

        column.AddChild(UiTheme.Text("ВАРИАНТЫ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        _variants = new VBoxContainer();
        _variants.AddThemeConstantOverride("separation", 6);
        column.AddChild(_variants);

        column.AddChild(UiTheme.Text("ЧТО ИЗМЕНИТСЯ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        _changes = new VBoxContainer();
        _changes.AddThemeConstantOverride("separation", 3);
        column.AddChild(_changes);

        _forecast = UiTheme.Text("", 14, null, UiTheme.TextPrimary);
        _forecast.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_forecast);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        column.AddChild(actions);
        _propose = new Button { Text = "Подготовить закон", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(220, 38) };
        _propose.AddThemeColorOverride("font_color", UiTheme.Ocean);
        _propose.AddThemeColorOverride("font_hover_color", UiTheme.Ocean);
        _propose.AddThemeColorOverride("font_disabled_color", UiTheme.TextMuted);
        _propose.AddThemeFontOverride("font", UiTheme.SemiBold);
        _propose.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.Accent, null, 10, 14, 8));
        _propose.AddThemeStyleboxOverride("hover", UiTheme.Box(UiTheme.AccentLight, null, 10, 14, 8));
        _propose.AddThemeStyleboxOverride("disabled", UiTheme.Box(UiTheme.ButtonBg, null, 10, 14, 8));
        _propose.Pressed += () =>
        {
            if (_countryId != null && _variantId != null)
                Proposed?.Invoke(_countryId, _lawId, _variantId);
        };
        actions.AddChild(_propose);
        _withdraw = new Button { Text = "Отозвать законопроект", FocusMode = FocusModeEnum.None };
        _withdraw.Pressed += () =>
        {
            if (_countryId != null)
                Withdrawn?.Invoke(_countryId, _lawId);
        };
        actions.AddChild(_withdraw);

        column.AddChild(new HSeparator());
        column.AddChild(UiTheme.Text("ЗАКОНОПРОЕКТЫ И РЕШЕНИЯ", 11, UiTheme.MapCountryFont, UiTheme.Accent));
        _bills = new VBoxContainer();
        _bills.AddThemeConstantOverride("separation", 3);
        column.AddChild(_bills);
        return scroll;
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

    /// <summary>Открыть экран сразу на нужном законе (для обхода скриншотов и подсказок).</summary>
    public void SelectLaw(string lawId, string? variant = null)
    {
        _lawId = lawId;
        _variantId = variant;
        foreach (var (id, (button, _)) in _rows)
            button.SetPressedNoSignal(id == lawId);
        if (_shown != null)
            Render(_shown, _shownPlayer);
    }

    public void Refresh(WorldSnapshot snapshot)
    {
        if (_countryId == null || !snapshot.Countries.TryGetValue(_countryId, out var country))
            return;
        var player = snapshot.PlayerCountry == _countryId;
        if (ReferenceEquals(country.Laws, _shown?.Laws) && ReferenceEquals(country.Government, _shown?.Government) && player == _shownPlayer)
            return;
        _shown = country;
        _shownPlayer = player;
        var record = _data.Countries.Countries.First(c => c.Id == _countryId);
        _title.Text = "Законы: " + record.Name;
        var p = country.Government?.Parliament;
        _subtitle.Text = (p is { Kind: "none" } ? "Парламента нет — законы вводятся указом" : $"{p?.Name}: правящий блок {p?.Ruling} из {p?.Seats} мест")
                         + (player ? "" : " · вы можете только смотреть: это не ваша страна");
        Render(country, player);
    }

    void Render(CountrySnapshot country, bool player)
    {
        if (country.Laws is not { } laws)
            return;
        foreach (var law in _data.Laws.Laws)
        {
            var (button, status) = _rows[law.Id];
            button.TooltipText = "Сейчас: " + law.Variant(laws.Current[law.Id]).Name;
            status.Text = laws.BillFor(law.Id) != null ? "готовится"
                : laws.Transitions.TryGetValue(law.Id, out var progress) ? $"вводится {Math.Round(progress * 100)}%"
                : laws.Current[law.Id] != laws.Start[law.Id] ? "изменён" : "";
        }

        var lawRecord = _data.Law(_lawId);
        var current = laws.Current[_lawId];
        var bill = laws.BillFor(_lawId);
        _variantId ??= bill?.Variant ?? current;
        var minister = country.Government?.Minister(lawRecord.Post);
        var post = _posts[lawRecord.Post];
        _lawTitle.Text = lawRecord.Name;
        _lawMeta.Text = $"{_data.Laws.Groups.First(g => g.Id == lawRecord.Group).Name} · готовит: {post.Ministry.ToLowerInvariant()}" +
                        (minister != null ? $" ({minister.Name}, компетентность {Math.Round(minister.Competence)})" : "") +
                        (lawRecord.Constitutional ? " · конституционный закон: нужно две трети парламента" : "");
        _lawDescription.Text = lawRecord.Description;

        foreach (var child in _variants.GetChildren())
            child.QueueFree();
        foreach (var v in lawRecord.Variants)
            _variants.AddChild(VariantCard(v, v.Id == current, v.Id == _variantId, bill?.Variant == v.Id));

        foreach (var child in _changes.GetChildren())
            child.QueueFree();
        if (_variantId == current)
            _changes.AddChild(UiTheme.Text("Это действующий вариант. Выберите другой, чтобы увидеть, что изменится.", 13, null, UiTheme.TextMuted));
        else
        {
            foreach (var line in LawText.EffectLines(_data, lawRecord, current, _variantId))
                _changes.AddChild(UiTheme.Text("• " + line, 14, null, UiTheme.TextSecondary));
            _changes.AddChild(AttitudeRow(LawText.AttitudeShifts(_data, lawRecord, current, _variantId)));
        }

        var influence = country.Classes?.Select(c => c.Influence).ToArray() ?? [];
        var support = _variantId == current ? 0.5 : Legislation.Support(_data, lawRecord, current, _variantId, influence);
        var months = Legislation.DraftingMonths(_data.LawsBalance, lawRecord, minister?.Competence ?? 50, minister?.Traits.Contains("reformer") == true);
        _forecast.Text = _variantId == current ? "" : ForecastText(country, lawRecord, current, _variantId, support, months);

        _propose.Visible = player;
        _propose.Disabled = !player || bill != null || _variantId == current;
        _propose.TooltipText = bill != null ? "По этому закону уже готовится проект" : _variantId == current ? "Этот вариант уже действует" : "";
        _withdraw.Visible = player && bill != null;

        foreach (var child in _bills.GetChildren())
            child.QueueFree();
        var all = laws.Bills.Concat(laws.Finished.AsEnumerable().Reverse()).ToList();
        if (all.Count == 0)
            _bills.AddChild(UiTheme.Text("Законопроектов пока нет.", 13, null, UiTheme.TextMuted));
        foreach (var b in all)
            _bills.AddChild(BillLine(b));
    }

    string ForecastText(CountrySnapshot country, LawRecord law, string from, string to, double support, int months)
    {
        var text = $"Одобрение в обществе: {Fmt.Value("percent", support * 100)}. Подготовка займёт около {Fmt.Plural(months, "месяца", "месяцев", "месяцев")}.";
        var groups = country.Politics?.Groups ?? [];
        text += GroupsText(groups, law, from, to);
        if (country.Government?.Parliament is not { } p)
            return text;
        // Каждая фракция смотрит на своих избирателей; блоки без партии — на общество в целом. Лоббисты сдвигают всех.
        var lobby = InterestGroupsModel.Lobby(_data, groups.Select(g => (g.Id, g.Influence)), law, from, to);
        var factions = p.Factions.Select(f => new FactionSupport(f.Party, f.Seats, f.Ruling,
            Math.Clamp((country.Politics?.Party(f.Party) is { } party ? Legislation.PartySupport(_data, law, from, to, party.Voters) : support) + lobby, 0, 1))).ToList();
        var vote = ParliamentVote.Forecast(_data.GovernmentBalance.Parliament, p.Kind, p.Seats, p.Discipline, factions, law.Constitutional);
        if (vote.Decree)
            return text + " Парламента нет: закон вступит в силу указом.";
        var verdict = vote.Passed ? "скорее всего пройдёт" : "скорее всего не пройдёт";
        return text + $" Прогноз голосования: за {vote.Yes}, против {vote.No}, нужно {vote.Required} — {verdict}.";
    }

    /// <summary>Какие группы интересов за, какие против (по силе отношения и влиянию).</summary>
    string GroupsText(IReadOnlyList<GroupSnapshot> groups, LawRecord law, string from, string to)
    {
        var stances = groups
            .Select(g => (Group: _data.InterestGroups.Groups.FirstOrDefault(r => r.Id == g.Id), g.Influence))
            .Where(x => x.Group != null)
            .Select(x => (x.Group!.Name, x.Influence, Stance: InterestGroupsModel.Stance(_data, x.Group, law, from, to)))
            .ToList();
        string Names(IEnumerable<(string Name, double Influence, double Stance)> list) =>
            string.Join(", ", list.OrderByDescending(x => x.Influence * Math.Abs(x.Stance)).Select(x => x.Name.ToLowerInvariant()));
        var pro = stances.Where(x => x.Stance >= 0.5).ToList();
        var contra = stances.Where(x => x.Stance <= -0.5).ToList();
        if (pro.Count == 0 && contra.Count == 0)
            return " Группам интересов закон безразличен.";
        var parts = new List<string>();
        if (pro.Count > 0)
            parts.Add("за — " + Names(pro));
        if (contra.Count > 0)
            parts.Add("против — " + Names(contra));
        return " Группы интересов: " + string.Join("; ", parts) + ".";
    }

    Control VariantCard(LawVariantRecord v, bool current, bool selected, bool drafting)
    {
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        var border = selected ? UiTheme.Accent : UiTheme.Hairline;
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, selected ? 0.07f : 0.03f), border, 10, 14, 9));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        card.AddChild(column);
        var top = new HBoxContainer();
        column.AddChild(top);
        var name = UiTheme.Text(v.Name, 15, UiTheme.SemiBold, selected ? UiTheme.AccentLight : UiTheme.TextPrimary);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(name);
        if (current)
            top.AddChild(UiTheme.Text("действует", 12, UiTheme.Medium, Color.FromHtml("#7fc49a")));
        if (drafting)
            top.AddChild(UiTheme.Text("готовится", 12, UiTheme.Medium, UiTheme.Accent));
        var description = UiTheme.Text(v.Description, 13, null, UiTheme.TextSecondary);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(description);
        var id = v.Id;
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                _variantId = id;
                if (_shown != null)
                    Render(_shown, _shownPlayer);
            }
        };
        return card;
    }

    Control AttitudeRow(double[] shifts)
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        var classes = _data.Classes.Classes;
        var any = false;
        for (var k = 0; k < shifts.Length && k < classes.Count; k++)
        {
            if (Math.Abs(shifts[k]) < 1e-9)
                continue;
            any = true;
            var chip = new PanelContainer();
            var color = shifts[k] > 0 ? Color.FromHtml("#7fc49a") : UiTheme.Critical;
            chip.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(color, 0.14f), new Color(color, 0.4f), 8, 8, 3));
            var label = UiTheme.Text($"{classes[k].ShortName ?? classes[k].Name} {Fmt.Signed(shifts[k], 0)}", 12, UiTheme.Medium, color);
            chip.AddChild(label);
            chip.TooltipText = shifts[k] > 0 ? "Этому классу закон понравится" : "Этому классу закон не понравится";
            chip.MouseFilter = MouseFilterEnum.Stop;
            flow.AddChild(chip);
        }
        if (!any)
            flow.AddChild(UiTheme.Text("Классы отнесутся равнодушно.", 13, null, UiTheme.TextMuted));
        return flow;
    }

    Control BillLine(BillSnapshot b)
    {
        var law = _data.Law(b.Law);
        var text = $"«{law.Name}: {law.Variant(b.Variant).Name.ToLowerInvariant()}» — {LawText.Stage(b.Stage)}";
        if (b.Stage == "drafting")
            text += $", будет готов к {Fmt.Date(b.Ready)}";
        else if (b.Vote is { Decree: true })
            text += " указом";
        else if (b.Vote is { } v)
            text += $": за {v.Yes}, против {v.No}";
        if (b.Decided is { } d)
            text += $" ({Fmt.Date(d)})";
        var color = b.Stage switch { "passed" => Color.FromHtml("#7fc49a"), "rejected" => UiTheme.Critical, "drafting" => UiTheme.Accent, _ => UiTheme.TextMuted };
        var label = UiTheme.Text(text, 13, null, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }
}
