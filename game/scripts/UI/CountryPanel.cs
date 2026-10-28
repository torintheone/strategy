using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>Досье страны справа: общие сведения и показатели с подсказками «почему».</summary>
public partial class CountryPanel : PanelContainer
{
    static readonly (string Title, string[] Stats)[] Sections =
    [
        ("Экономика", ["gdp_usd", "gdp_per_capita_usd", "gdp_ppp_per_capita", "gdp_growth_pct", "inflation_pct", "unemployment_pct",
            "agriculture_pct_gdp", "industry_pct_gdp", "exports_pct_gdp", "imports_pct_gdp"]),
        ("Государство", ["gov_debt_pct_gdp", "tax_revenue_pct_gdp", "military_pct_gdp"]),
        ("Население", ["population", "labor_force", "life_expectancy", "fertility_rate", "urban_pct", "age_0_14_pct",
            "age_65_plus_pct", "gini", "electricity_access_pct"]),
    ];

    GameData _data = null!;
    Label _name = null!;
    Label _official = null!;
    GridContainer _facts = null!;
    Button _play = null!;
    Label _playing = null!;
    readonly Dictionary<string, (Control Row, Label Value)> _rows = new();
    string? _countryId;

    public event Action? Closed;
    public event Action<string>? PlayAs;

    public string? CountryId => _countryId;

    public void Setup(GameData data)
    {
        _data = data;
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

    public void ShowCountry(string id, WorldSnapshot snapshot)
    {
        _countryId = id;
        var record = _data.Countries.Countries.First(c => c.Id == id);
        _name.Text = record.Name;
        _official.Text = record.OfficialName;

        foreach (var child in _facts.GetChildren())
            child.QueueFree();
        var politics = _data.Politics.Countries.FirstOrDefault(p => p.Id == id);
        var country = snapshot.Countries[id];
        AddFact("Столица", record.Capital);
        AddFact("Регион", record.Subregion);
        AddFact("Строй", _data.Politics.Systems.GetValueOrDefault(WorldFactory.SystemId(country.System), "—"),
            politics is null ? null : PoliticsTooltip(politics));
        AddFact("Доход", _data.MapModes.IncomeGroups.FirstOrDefault(g => g.Id == WorldFactory.IncomeId(country.Income))?.Name ?? "—",
            "Группа дохода по классификации Всемирного банка на 2026 финансовый год (ВНД на душу населения за 2024 год).");
        Visible = true;
        Refresh(snapshot);
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
