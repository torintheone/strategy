using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>Досье страны справа: выбранный регион, общие сведения, показатели с подсказками «почему» и список регионов.</summary>
public partial class CountryPanel : PanelContainer
{
    static readonly (string Title, string[] Stats)[] Sections =
    [
        ("Экономика", ["gdp_usd", "gdp_per_capita_usd", "gdp_ppp_per_capita", "gdp_growth_pct", "inflation_pct", "unemployment_pct",
            "investment_pct_gdp", "exports_pct_gdp", "imports_pct_gdp", "current_account_pct_gdp"]),
        ("Деньги и долг", ["policy_rate_pct", "exchange_rate_change_pct", "credit_rating", "bond_yield_pct", "gov_debt_pct_gdp",
            "interest_pct_gdp", "defaults"]),
        ("Бюджет", ["budget_balance_pct_gdp", "gov_revenue_pct_gdp", "gov_spending_pct_gdp", "tax_revenue_pct_gdp", "military_pct_gdp"]),
        ("Структура экономики", ["agriculture_pct_gdp", "industry_pct_gdp", "manufacturing_pct_gdp", "services_pct_gdp",
            "resource_rents_pct_gdp", "tourism_pct_gdp", "employment_agriculture_pct", "employment_industry_pct", "employment_services_pct"]),
        ("Ориентиры прогноза МВФ", ["gdp_growth_trend_pct", "inflation_anchor_pct", "unemployment_natural_pct"]),
        ("Население", ["population", "labor_force", "life_expectancy", "fertility_rate", "urban_pct", "age_0_14_pct",
            "age_65_plus_pct", "gini", "electricity_access_pct"]),
    ];

    GameData _data = null!;
    Dictionary<string, RegionRecord> _regions = null!;
    Label _name = null!;
    Label _official = null!;
    Label _status = null!;
    VBoxContainer _regionBox = null!;
    Label _regionTitle = null!;
    Label _regionLine = null!;
    Label _regionControl = null!;
    GridContainer _facts = null!;
    Button _play = null!;
    Label _playing = null!;
    Label _regionsHeader = null!;
    VBoxContainer _regionList = null!;
    readonly Dictionary<string, (Control Row, Label Value)> _rows = new();
    string? _countryId;
    string? _regionId;

    public event Action? Closed;
    public event Action<string>? PlayAs;
    public event Action<string>? RegionChosen;

    public string? CountryId => _countryId;

    public void Setup(GameData data)
    {
        _data = data;
        _regions = data.Regions.Regions.ToDictionary(r => r.Id, StringComparer.Ordinal);
        SetAnchorsPreset(LayoutPreset.RightWide);
        OffsetLeft = -420;
        OffsetRight = -16;
        OffsetTop = 68;
        OffsetBottom = -16;
        MouseFilter = MouseFilterEnum.Stop;

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        AddChild(column);

        var header = new HBoxContainer();
        column.AddChild(header);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        header.AddChild(titles);
        _name = UiTheme.Text("", 26, UiTheme.SerifBold);
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titles.AddChild(_name);
        _official = UiTheme.Text("", 13, null, UiTheme.TextMuted);
        _official.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titles.AddChild(_official);
        var close = new Button { Text = "×", Flat = true, TooltipText = "Закрыть (Esc)", FocusMode = FocusModeEnum.None };
        close.AddThemeFontSizeOverride("font_size", 22);
        close.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        close.Pressed += () => Closed?.Invoke();
        header.AddChild(close);

        _status = UiTheme.Text("", 13, null, UiTheme.AccentLight);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_status);

        // Выбранный на карте регион.
        _regionBox = new VBoxContainer();
        _regionBox.AddThemeConstantOverride("separation", 0);
        var regionPanel = new PanelContainer();
        regionPanel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.PanelRaised, UiTheme.PanelBorder, 4, 10, 8));
        regionPanel.AddChild(_regionBox);
        column.AddChild(regionPanel);
        _regionTitle = UiTheme.Text("", 16, UiTheme.SemiBold);
        _regionTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _regionLine = UiTheme.Text("", 13, null, UiTheme.TextSecondary);
        _regionLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _regionControl = UiTheme.Text("", 13, UiTheme.Medium, UiTheme.Critical);
        _regionBox.AddChild(_regionTitle);
        _regionBox.AddChild(_regionLine);
        _regionBox.AddChild(_regionControl);

        _facts = new GridContainer { Columns = 2 };
        _facts.AddThemeConstantOverride("h_separation", 14);
        _facts.AddThemeConstantOverride("v_separation", 4);
        column.AddChild(_facts);

        _play = new Button { Text = "Играть за эту страну", FocusMode = FocusModeEnum.None };
        _play.AddThemeColorOverride("font_color", UiTheme.Accent);
        _play.Pressed += () =>
        {
            if (_countryId != null)
                PlayAs?.Invoke(_countryId);
        };
        column.AddChild(_play);
        _playing = UiTheme.Text("Вы управляете этой страной", 14, UiTheme.Medium, UiTheme.Accent);
        column.AddChild(_playing);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(body);

        foreach (var (title, stats) in Sections)
        {
            body.AddChild(new HSeparator());
            body.AddChild(UiTheme.Text(title.ToUpperInvariant(), 12, UiTheme.SemiBold, UiTheme.TextMuted));
            foreach (var id in stats)
                AddRow(body, id);
        }
        body.AddChild(new HSeparator());
        _regionsHeader = UiTheme.Text("", 12, UiTheme.SemiBold, UiTheme.TextMuted);
        body.AddChild(_regionsHeader);
        _regionList = new VBoxContainer();
        _regionList.AddThemeConstantOverride("separation", 0);
        body.AddChild(_regionList);
        body.AddChild(new HSeparator());
        var note = UiTheme.Text("≈ — оценка: в источниках нет данных по стране. Наведите курсор на строку, чтобы увидеть источник и год.",
            12, null, UiTheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(note);
    }

    void AddRow(VBoxContainer body, string statId)
    {
        var def = _data.Indicator(statId);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop };
        var label = UiTheme.Text(def.Name, 14, null, UiTheme.TextSecondary);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(label);
        var value = UiTheme.Text("", 14, UiTheme.Medium);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(value);
        body.AddChild(row);
        _rows[statId] = (row, value);
    }

    public void ShowCountry(string id, string? regionId, WorldSnapshot snapshot)
    {
        var changedCountry = id != _countryId;
        _countryId = id;
        _regionId = regionId;
        var record = _data.Countries.Countries.First(c => c.Id == id);
        _name.Text = record.Name;
        _official.Text = record.OfficialName;
        _status.Text = record.UnMember ? "" : "Не член ООН. " + record.StatusNote;
        _status.Visible = !record.UnMember;

        if (changedCountry)
        {
            foreach (var child in _facts.GetChildren())
                child.QueueFree();
            var politics = _data.Politics.Countries.FirstOrDefault(p => p.Id == id);
            var country = snapshot.Countries[id];
            AddFact("Столица", record.Capital);
            AddFact("Регион мира", record.Subregion);
            AddFact("Строй", _data.Politics.Systems.GetValueOrDefault(WorldFactory.SystemId(country.System), "—"),
                politics is null ? null : PoliticsTooltip(politics));
            AddFact("Доход", _data.MapModes.IncomeGroups.FirstOrDefault(g => g.Id == WorldFactory.IncomeId(country.Income))?.Name ?? "—",
                "Группа дохода по классификации Всемирного банка на 2026 финансовый год (ВНД на душу населения за 2024 год).");
            if (snapshot.Currencies.GetValueOrDefault(id) is { } currency)
                AddFact("Валюта", CurrencyText(currency), CurrencyTooltip(currency));
            FillRegionList(id);
        }
        Visible = true;
        Refresh(snapshot);
    }

    void FillRegionList(string countryId)
    {
        foreach (var child in _regionList.GetChildren())
            child.QueueFree();
        var own = _data.Regions.Regions.Where(r => r.Country == countryId).OrderByDescending(r => r.Population).ToList();
        _regionsHeader.Text = $"РЕГИОНЫ: {own.Count}";
        foreach (var r in own)
        {
            var button = new Button
            {
                Flat = true,
                FocusMode = FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left,
                Text = r.Name,
                TooltipText = RegionTooltip(r),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true,
            };
            button.AddThemeFontSizeOverride("font_size", 13);
            var regionId = r.Id;
            button.Pressed += () => RegionChosen?.Invoke(regionId);
            var row = new HBoxContainer();
            row.AddChild(button);
            var pop = UiTheme.Text((r.PopulationSource == "estimate" ? "≈" : "") + Fmt.People(r.Population), 13, null, UiTheme.TextMuted);
            pop.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(pop);
            _regionList.AddChild(row);
        }
    }

    string CurrencyName(string code) =>
        _data.Currencies.Currencies.FirstOrDefault(c => c.Id == code)?.Name ?? code;

    string CurrencyText(CurrencySnapshot c) => $"{CurrencyName(c.Code)} ({c.Code})";

    string CurrencyTooltip(CurrencySnapshot c)
    {
        var regime = c.Floating
            ? "Плавающий курс: центробанк сам ставит ключевую ставку по инфляции и состоянию экономики, курс к доллару меняется."
            : $"Курс привязан к валюте {CurrencyName(c.Anchor!)}: ставка следует за ставкой {c.Anchor}, курс к ней не меняется.";
        var shared = c.Users > 1
            ? $"\nВалютой пользуются {c.Users} стран(ы): ставку задаёт общий центробанк ({c.Bank ?? "по стране-эмитенту"}) по средней инфляции и экономике всех."
            : "";
        return regime + shared;
    }

    static string RegionTooltip(RegionRecord r)
    {
        var lines = new List<string>
        {
            r.Name,
            $"Население: {Fmt.People(r.Population)}, площадь: {Fmt.Number(r.AreaKm2, 0)}{Fmt.Nbsp}км²",
        };
        if (!string.IsNullOrEmpty(r.Capital))
            lines.Add("Главный город: " + r.Capital);
        lines.Add(r.PopulationSource == "estimate"
            ? "Население оценено по средней плотности страны: в GeoNames данных нет."
            : "Население по GeoNames, приведено к населению страны по Всемирному банку.");
        return string.Join('\n', lines);
    }

    static string PoliticsTooltip(PoliticsRecord p)
    {
        var text = "Тип системы задаёт, кто формирует правительство и как меняется власть.";
        if (!string.IsNullOrEmpty(p.Note))
            text += "\n\nОсобенность: " + p.Note;
        if (!p.Reviewed)
            text += "\n\nЧерновик классификации, ещё не проверен.";
        return text;
    }

    void AddFact(string caption, string value, string? tooltip = null)
    {
        _facts.AddChild(UiTheme.Text(caption, 13, null, UiTheme.TextMuted));
        var label = UiTheme.Text(value, 14);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(250, 0);
        if (tooltip != null)
        {
            label.MouseFilter = MouseFilterEnum.Stop;
            label.TooltipText = tooltip;
        }
        _facts.AddChild(label);
    }

    /// <summary>Обновить значения из нового снимка мира.</summary>
    public void Refresh(WorldSnapshot snapshot)
    {
        if (_countryId == null || !snapshot.Countries.TryGetValue(_countryId, out var country))
            return;
        var isPlayer = snapshot.PlayerCountry == _countryId;
        _play.Visible = !isPlayer;
        _playing.Visible = isPlayer;

        var region = _regionId is null ? null : _regions.GetValueOrDefault(_regionId);
        _regionBox.GetParent<Control>().Visible = region != null;
        if (region != null)
        {
            _regionTitle.Text = region.Name;
            var capital = string.IsNullOrEmpty(region.Capital) ? "" : $" · {region.Capital}";
            _regionLine.Text = $"{(region.PopulationSource == "estimate" ? "≈" : "")}{Fmt.People(region.Population)} жителей · " +
                               $"{Fmt.Number(region.AreaKm2, 0)}{Fmt.Nbsp}км²{capital}";
            _regionLine.TooltipText = RegionTooltip(region);
            _regionLine.MouseFilter = MouseFilterEnum.Stop;
            var controller = snapshot.ControllerOf(region.Id, region.Country);
            _regionControl.Visible = controller != region.Country;
            if (controller != region.Country)
                _regionControl.Text = "Под контролем: " + _data.Countries.Countries.First(c => c.Id == controller).Name;
        }

        foreach (var (id, (row, value)) in _rows)
        {
            var def = _data.Indicator(id);
            var stat = country.Stat(id);
            value.Text = stat == null ? "—" : (stat.IsEstimate ? "≈" : "") + Fmt.Value(def.Format, stat.Value, def.Unit);
            value.AddThemeColorOverride("font_color", stat is { IsEstimate: false } ? UiTheme.TextPrimary : UiTheme.TextMuted);
            row.TooltipText = Explain.Stat(def, stat);
        }
    }
}
