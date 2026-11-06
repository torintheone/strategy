using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Economy.Systems;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Экран «Дипломатия» (страна игрока), четыре вкладки. «Страны»: слева — все страны с отношениями и долей в нашей
/// торговле, справа — выбранная: из чего сложились отношения, мощь, действия, договоры, санкции, резолюции Совета Наций
/// с прогнозом голосования. «Блоки»: торговые и военные блоки — вступить или выйти. «Совет Наций»: Совет безопасности,
/// санкции Совета, последние резолюции. «Валютный совет»: кредит и программа.
/// </summary>
public partial class DiplomacyScreen : GlassPanel
{
    static readonly Color Good = Color.FromHtml("#7fc49a");
    static readonly string[] Tabs = ["countries", "blocs", "council", "fund"];

    GameData _data = null!;
    Label _title = null!;
    Label _subtitle = null!;
    HBoxContainer _tabs = null!;
    Control _countries = null!;
    ScrollContainer _page = null!;
    VBoxContainer _pageContent = null!;
    VBoxContainer _list = null!;
    VBoxContainer _detail = null!;
    VBoxContainer _target = null!;
    string? _partner;
    string _tab = "countries";
    bool _byTrade;
    string? _shown;
    DiplomacySnapshot? _snapshot;
    IReadOnlyDictionary<string, CountrySnapshot> _world = new Dictionary<string, CountrySnapshot>();

    public event Action? Closed;
    /// <summary>
    /// Действие: цель (партнёр или блок) и что сделать: visit, aid, expel, propose:тип, cancel:тип, sanction:уровень,
    /// lift, resolution:вид, join, leave, loan.
    /// </summary>
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

        _tabs = new HBoxContainer();
        _tabs.AddThemeConstantOverride("separation", 6);
        column.AddChild(_tabs);
        foreach (var (id, text) in new[] { ("countries", "Страны"), ("blocs", "Блоки"), ("council", _data.Diplomacy.Council.Name), ("fund", _data.Diplomacy.Fund.Name) })
        {
            var tab = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) };
            tab.AddThemeFontSizeOverride("font_size", 14);
            tab.SetMeta("tab", id);
            tab.Pressed += () => ShowTab(id);
            _tabs.AddChild(tab);
        }

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 20);
        column.AddChild(body);
        _countries = body;

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

        _page = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false,
        };
        column.AddChild(_page);
        _pageContent = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _pageContent.AddThemeConstantOverride("separation", 10);
        _page.AddChild(_pageContent);
        _target = _detail;
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
    public void Open(WorldSnapshot snapshot, string? partner, string? tab = null)
    {
        if (partner != null && partner != snapshot.PlayerCountry)
            _partner = partner;
        if (tab != null && Tabs.Contains(tab))
            _tab = tab;
        else if (partner != null)
            _tab = "countries";
        _shown = null;
        var appearing = !Visible;
        Visible = true;
        Refresh(snapshot);
        if (appearing)
            AnimateIn(new Vector2(0, 0.1f), 0.2f, 0.985f);
    }

    void ShowTab(string tab)
    {
        _tab = tab;
        _shown = null;
        Render();
    }

    public void Refresh(WorldSnapshot snapshot)
    {
        if (snapshot.Diplomacy is not { } diplomacy)
            return;
        // Перерисовываем, когда что-то изменилось: месяц, вкладка, выбранная страна, отношения, договоры и санкции
        // с любым партнёром, блоки, Совет Наций, Валютный совет.
        var selected = diplomacy.Partners.FirstOrDefault(p => p.Id == _partner);
        var state = new HashCode();
        foreach (var p in diplomacy.Partners)
        {
            state.Add(Math.Round(p.Relations, 1));
            state.Add(p.Treaties.Count);
            state.Add(p.CanVisit);
            state.Add(p.OurSanction);
            state.Add(p.TheirSanction);
        }
        foreach (var b in diplomacy.Blocs)
        {
            state.Add(b.Member);
            state.Add(b.Accepts);
            state.Add(b.Members.Count);
        }
        state.Add(diplomacy.Council.Resolutions.Count);
        state.Add(diplomacy.Council.Resolutions.FirstOrDefault()?.Date);
        state.Add(diplomacy.Council.NextProposal);
        state.Add(diplomacy.Council.Sanctioned.Count);
        state.Add(diplomacy.Fund.Program?.Status);
        state.Add(diplomacy.Fund.Offer.Accepts);
        var signature = $"{snapshot.Date:yyyy-MM}|{_tab}|{_partner}|{_byTrade}|{selected?.Relations:F2}|{string.Join(",", selected?.Treaties ?? [])}|{state.ToHashCode()}";
        if (signature == _shown)
            return;
        _shown = signature;
        _snapshot = diplomacy;
        _world = snapshot.Countries;
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
        var power = _world.GetValueOrDefault(diplomacy.Country)?.Stat("power_rank") is { } rank ? $" · мощь: {rank.Value:0}-е место в мире" : "";
        _subtitle.Text = $"Друзей (отношения выше +30): {friends} · недругов (ниже −30): {enemies} · договоров: {treaties}{power}.";
        foreach (var child in _tabs.GetChildren().OfType<Button>())
        {
            var active = (string)child.GetMeta("tab") == _tab;
            child.AddThemeColorOverride("font_color", active ? UiTheme.Accent : UiTheme.TextSecondary);
            child.AddThemeColorOverride("font_hover_color", active ? UiTheme.AccentLight : UiTheme.TextPrimary);
        }
        _countries.Visible = _tab == "countries";
        _page.Visible = _tab != "countries";
        switch (_tab)
        {
            case "countries":
                _partner ??= diplomacy.Partners.MaxBy(p => p.TradeShare)?.Id;
                RenderList(diplomacy);
                RenderDetail(diplomacy);
                break;
            case "blocs":
                RenderBlocs(diplomacy);
                break;
            case "council":
                RenderCouncil(diplomacy);
                break;
            case "fund":
                RenderFund(diplomacy);
                break;
        }
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
            var label = UiTheme.Text(CountryName(p.Id) + (p.OurSanction != null || p.TheirSanction != null ? "  ⊘" : ""), 14, null, UiTheme.TextPrimary);
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
        _target = _detail;
        if (diplomacy.Partners.FirstOrDefault(p => p.Id == _partner) is not { } p)
            return;
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 16);
        var name = UiTheme.Text(CountryName(p.Id), 24, UiTheme.SerifBold);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(name);
        head.AddChild(UiTheme.Text("отношения " + Fmt.Signed(p.Relations, 0), 22, UiTheme.SemiBold, Tone(p.Relations)));
        _detail.AddChild(head);
        var partner = _world.GetValueOrDefault(p.Id);
        var common = _data.Diplomacy.Blocs.Where(b => partner?.Blocs?.Contains(b.Id) == true
                                                       && _world.GetValueOrDefault(diplomacy.Country)?.Blocs?.Contains(b.Id) == true).Select(b => b.Name).ToList();
        var power = partner?.Stat("power_index") is { } index && partner.Stat("power_rank") is { } rank
            ? $"Индекс мощи {index.Value:0} ({rank.Value:0}-е место в мире). "
            : "";
        _detail.AddChild(Wrapped(power + $"Доля в нашей торговле — {Fmt.Value("percent", p.TradeShare * 100)}. " +
                                 (common.Count > 0 ? $"Мы вместе в {string.Join(", ", common.Select(Fmt.Quote))}. " : "") +
                                 "Отношения складываются из торговли, сходства строя и взглядов правящих партий, соседства, истории, блоков, санкций, договоров и событий; " +
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

        Section("ДЕЙСТВИЯ");
        var b = _data.DiplomacyBalance;
        Action("Визит главы", $"Отношения +{b.VisitRelations:0}. К одной стране — не чаще раза в {b.VisitMonths} месяцев; визит к недругу (ниже {b.VisitCriticismBelow:0}) критикуют дома.",
            p.CanVisit ? "Поехать" : null, p.CanVisit ? null : "Вы недавно были там с визитом.", "visit");
        Action("Помощь", $"Подарить {Fmt.Number(b.AidShare, 2)}% нашего ВВП: наш долг растёт, их — уменьшается. Чем заметнее помощь для их экономики, тем сильнее растут отношения.",
            "Помочь", null, "aid");
        Action("Выслать дипломатов", $"Сигнал недовольства: отношения −{b.ExpelRelations:0}.", "Выслать", null, "expel", critical: true);

        Section("ДОГОВОРЫ");
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

        Section("САНКЦИИ");
        var ours = p.OurSanction is { } our ? $"Наши санкции: {DiplomacyModel.SanctionName(our)}." : "Наших санкций нет.";
        var theirs = p.TheirSanction is { } their ? $" Их санкции против нас: {DiplomacyModel.SanctionName(their)}." : " Их санкций против нас нет.";
        _detail.AddChild(Wrapped(ours + theirs, 13, p.TheirSanction != null ? UiTheme.Critical : UiTheme.TextSecondary));
        if (p.OurSanction != "partial")
            Action("Частичные санкции", $"Торговля с ними × {Fmt.Number(1 - b.SanctionPartial, 1)} (и у нас тоже), им дороже занимать; отношения −{b.SanctionRelations / 2:0}. " +
                                        "Если своих санкций у них нет, они ответят частичными.", "Ввести", null, "sanction:partial");
        if (p.OurSanction != "full")
            Action("Жёсткие санкции", $"Торговля с ними × {Fmt.Number(1 - b.SanctionFull, 1)} (и у нас тоже), им труднее занимать и расти; отношения −{b.SanctionRelations:0}.",
                "Ввести", null, "sanction:full", critical: true);
        if (p.OurSanction != null)
            Action("Снять санкции", "Торговля восстановится; ответные санкции они снимут.", "Снять", null, "lift", active: true);

        Section(_data.Diplomacy.Council.Name.ToUpperInvariant());
        foreach (var r in _data.Diplomacy.Council.Resolutions)
        {
            var (allowed, why) = p.Resolutions?.GetValueOrDefault(r.Id) ?? (false, "");
            var text = r.Description;
            if (allowed)
            {
                var vote = CouncilVoting.Count(diplomacy.Council.Votes, b, r, diplomacy.Country, p.Id);
                var chamber = r.Chamber == "security" ? "Совет безопасности" : "Общее собрание";
                text += $" Голосует {chamber}. Прогноз: за {vote.Yes}, против {vote.No}, воздержатся {vote.Abstain}" +
                        (vote.Vetoes.Count > 0 ? $"; вето — {string.Join(", ", vote.Vetoes.Select(CountryName))}" : "") +
                        (vote.Passed ? " — пройдёт." : " — не пройдёт.");
            }
            else
                text += " " + why;
            Action(r.Name, text, allowed ? "Вынести" : null, allowed ? null : "Нельзя.", "resolution:" + r.Id);
        }
    }

    void RenderBlocs(DiplomacySnapshot diplomacy)
    {
        Clear(_pageContent);
        _target = _pageContent;
        var b = _data.DiplomacyBalance;
        _pageContent.AddChild(Wrapped("Блоки — с реальным составом на 1 января 2026 года. Члены торгового блока торгуют друг с другом свободнее, " +
                                      "военного — защищают друг друга; соперничающие военные блоки враждуют. Вступить можно, если члены «за»: " +
                                      $"отношения не ниже +{b.JoinRelations:0} (с военным блоком — +{b.JoinMilitaryRelations:0}) и никаких санкций. " +
                                      $"Выход портит отношения с каждым членом на {b.LeaveRelations:0}.", 13, UiTheme.TextSecondary));
        foreach (var military in new[] { true, false })
        {
            Section(military ? "ВОЕННЫЕ БЛОКИ" : "ТОРГОВЫЕ БЛОКИ");
            foreach (var bloc in _data.Diplomacy.Blocs.Where(x => x.Military == military))
            {
                if (diplomacy.Blocs.FirstOrDefault(x => x.Id == bloc.Id) is not { } state)
                    continue;
                var members = string.Join(", ", state.Members.Select(CountryName));
                var title = $"{bloc.Name} — {Fmt.Plural(state.Members.Count, "страна", "страны", "стран")}";
                var text = bloc.Description + (state.Members.Count > 0 ? $" Члены: {members}." : "") + (state.Member ? "" : " " + state.Reason);
                if (state.Member)
                    Action(title + " · мы в блоке", text, "Выйти", null, "leave", critical: true, active: true, target: bloc.Id, color: bloc.Color);
                else
                    Action(title, text, state.Accepts ? "Вступить" : null, state.Accepts ? null : "Не примут.", "join", target: bloc.Id, color: bloc.Color);
            }
        }
    }

    void RenderCouncil(DiplomacySnapshot diplomacy)
    {
        Clear(_pageContent);
        _target = _pageContent;
        var council = diplomacy.Council;
        var me = diplomacy.Country;
        var status = council.Permanent.Contains(me) ? "Мы — постоянный член Совета безопасности с правом вето."
            : council.Seats.FirstOrDefault(s => s.Country == me) is { } seat ? $"Мы — выборный член Совета безопасности до конца {seat.Until} года."
            : "Мы не в Совете безопасности: голосуем в Общем собрании.";
        _pageContent.AddChild(Wrapped($"Все признанные страны — члены «{_data.Diplomacy.Council.Name}». Общее собрание осуждает и помогает простым большинством; " +
                                      $"Совет безопасности вводит и снимает санкции: нужно {_data.DiplomacyBalance.SecurityVotes} голосов из 15 и ни одного «против» у постоянных членов. " +
                                      "Каждый голосует по своим отношениям с автором и со страной; союзники своих не сдают. " + status, 13, UiTheme.TextSecondary));
        _pageContent.AddChild(Wrapped(council.NextProposal is { } next
            ? $"Следующую резолюцию можно вынести с {Fmt.Date(next)}."
            : "Резолюцию выносят на вкладке «Страны»: выберите страну — там же прогноз голосования.", 13, UiTheme.TextPrimary));
        Section("СОВЕТ БЕЗОПАСНОСТИ");
        _pageContent.AddChild(Wrapped("Постоянные члены (с правом вето): " + string.Join(", ", council.Permanent.Select(CountryName)) + ".", 13, UiTheme.TextPrimary));
        _pageContent.AddChild(Wrapped("Выборные: " + string.Join(", ", council.Seats.OrderBy(s => s.Until).Select(s => $"{CountryName(s.Country)} (до {s.Until})")) +
                                      ". Каждый год пятеро уступают места странам своих регионов.", 13, UiTheme.TextPrimary));
        Section("САНКЦИИ СОВЕТА");
        _pageContent.AddChild(Wrapped(council.Sanctioned.Count > 0
            ? string.Join(", ", council.Sanctioned.Select(CountryName)) + ": все члены ограничивают торговлю с ними и кредиты им."
            : "Санкций Совета нет.", 13, UiTheme.TextPrimary));
        Section("РЕЗОЛЮЦИИ");
        if (council.Resolutions.Count == 0)
            _pageContent.AddChild(Wrapped("Резолюций ещё не было.", 13, UiTheme.TextMuted));
        foreach (var r in council.Resolutions)
        {
            var record = _data.Diplomacy.Council.Resolution(r.Type);
            var result = r.Passed ? "принята" : r.Vetoes.Count > 0 ? $"не принята: вето — {string.Join(", ", r.Vetoes.Select(CountryName))}" : "не принята";
            var line = UiTheme.Text($"{Fmt.Date(r.Date)} · {CountryName(r.Proposer)}: «{record?.Name}: {CountryName(r.Target)}» — {result} (за {r.Yes}, против {r.No}, воздержались {r.Abstain})",
                13, null, r.Passed ? Good : UiTheme.TextSecondary);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _pageContent.AddChild(line);
        }
    }

    void RenderFund(DiplomacySnapshot diplomacy)
    {
        Clear(_pageContent);
        _target = _pageContent;
        var fund = diplomacy.Fund;
        var b = _data.DiplomacyBalance;
        _pageContent.AddChild(Wrapped($"«{_data.Diplomacy.Fund.Name}» даёт дешёвые кредиты странам, которым рынок даёт деньги дорого (рейтинг BB+ и ниже). " +
                                      "Члены голосуют весом своих экономик и против тех, с кем отношения плохие. " +
                                      $"Взамен — условия: через {b.FundTargetYears} года и до конца программы дефицит не больше {Fmt.Number(b.FundDeficit, 0)}% ВВП, без национализации.",
            13, UiTheme.TextSecondary));
        _pageContent.AddChild(Wrapped($"Наш рейтинг — {RatingSystem.Letter(fund.Rating)}, дефицит бюджета — {Fmt.Number(fund.Deficit, 1)}% ВВП.", 14, UiTheme.TextPrimary));
        if (fund.Program is { } p)
        {
            Section("ПРОГРАММА");
            var text = p.Status switch
            {
                "active" => $"Идёт с {Fmt.Date(p.Since)} по {Fmt.Date(p.Until)}: кредит {Fmt.Number(p.Amount * 100, 1)}% ВВП под {Fmt.Number(p.Rate * 100, 1)}%, " +
                            $"премия за риск ниже на {Fmt.Number(b.FundSpread, 1)} п. п. С {Fmt.Date(p.Deadline)} дефицит должен быть не больше {Fmt.Number(b.FundDeficit, 0)}% ВВП " +
                            $"(сейчас {Fmt.Number(fund.Deficit, 1)}%).",
                "done" => $"Программа {Fmt.Date(p.Since)} — {Fmt.Date(p.Until)} выполнена.",
                _ => $"Программу прекратили за нарушение условий; занимать дороже на {Fmt.Number(b.FundPenalty, 1)} п. п." +
                     (p.PenaltyUntil is { } until ? $" до {Fmt.Date(until)}." : "."),
            };
            _pageContent.AddChild(Wrapped(text, 13, p.Status == "failed" ? UiTheme.Critical : p.Status == "active" ? Good : UiTheme.TextPrimary));
        }
        Section("КРЕДИТ");
        Action("Попросить кредит", fund.Offer.Reason, fund.Offer.Accepts ? "Попросить" : null, fund.Offer.Accepts ? null : "Не дадут.", "loan",
            target: diplomacy.Country);
    }

    void Section(string text) => _target.AddChild(UiTheme.Text(text, 11, UiTheme.MapCountryFont, UiTheme.Accent));

    void Action(string title, string text, string? button, string? disabled, string action, bool critical = false, bool active = false,
        string? target = null, string? color = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        if (color != null)
        {
            var swatch = new ColorRect { Color = Color.FromHtml(color), CustomMinimumSize = new Vector2(6, 0) };
            row.AddChild(swatch);
        }
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
                if ((target ?? _partner) is { } who)
                    ActionChosen?.Invoke(who, action);
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
        _target.AddChild(row);
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
