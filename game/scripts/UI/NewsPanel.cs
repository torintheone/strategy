using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.News;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Список новостей над лентой: все, только свои («Дома») или только чужие («В мире»), свежие сверху. Щелчок по
/// новости показывает её страну на карте.
/// </summary>
public partial class NewsPanel : GlassPanel
{
    /// <summary>Сколько новостей в списке: больше не прочитать, а строить список приходится на ходу.</summary>
    const int MaxRows = 80;

    GameData _data = null!;
    Dictionary<string, string> _names = null!;
    VBoxContainer _list = null!;
    ScrollContainer _scroll = null!;
    Label _count = null!;
    readonly Dictionary<string, Button> _filters = new();
    string _filter = "all";
    int _version = -1;
    string? _player;
    string? _shownFilter;

    /// <summary>Щелчок по новости: страна, о которой она.</summary>
    public event Action<string>? CountryChosen;
    public event Action? Closed;

    public void Setup(GameData data)
    {
        _data = data;
        _names = data.Countries.Countries.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        SetAnchorsPreset(LayoutPreset.BottomWide);
        OffsetLeft = 494;
        OffsetRight = -12;
        OffsetBottom = -66;
        OffsetTop = -66 - 560;
        MouseFilter = MouseFilterEnum.Stop;
        InitGlass(radius: 14, padH: 18, padV: 14, tint: 0.92f);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        Body.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        column.AddChild(header);
        header.AddChild(UiTheme.Text("Новости", 22, UiTheme.SerifBold));
        header.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });
        var group = new ButtonGroup();
        foreach (var (id, name, tip) in new[]
                 {
                     ("all", "Все", "Все новости"), ("home", "Дома", "Новости вашей страны и то, что её касается"),
                     ("world", "В мире", "Важное в других странах и на мировых рынках"),
                 })
        {
            var button = new Button { Text = name, ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.None, TooltipText = tip, ButtonPressed = id == _filter };
            button.AddThemeFontSizeOverride("font_size", 13);
            button.Pressed += () =>
            {
                _filter = id;
                Rebuild();
            };
            header.AddChild(button);
            _filters[id] = button;
        }
        _count = UiTheme.Text("", 13, null, UiTheme.TextMuted);
        _count.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _count.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(_count);
        var close = new Button { Text = "×", Flat = true, TooltipText = "Закрыть (Esc)", FocusMode = FocusModeEnum.None };
        close.AddThemeFontSizeOverride("font_size", 22);
        close.Pressed += () => Closed?.Invoke();
        header.AddChild(close);
        column.AddChild(new HSeparator());

        _scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(_scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 2);
        _scroll.AddChild(_list);
    }

    IReadOnlyList<NewsItem> _items = [];

    /// <summary>Выбрать, что показывать: all, home или world.</summary>
    public void SetFilter(string filter)
    {
        _filter = filter;
        foreach (var (id, button) in _filters)
            button.SetPressedNoSignal(id == filter);
        Rebuild();
    }

    public void Refresh(NewsSnapshot news, string? player)
    {
        if (news.Version == _version && player == _player && _shownFilter == _filter)
            return;
        _version = news.Version;
        _player = player;
        _items = news.Items;
        Rebuild();
    }

    void Rebuild()
    {
        _shownFilter = _filter;
        foreach (var child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }
        var shown = 0;
        var total = 0;
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if ((_filter == "home" && !item.Home) || (_filter == "world" && item.Home))
                continue;
            total++;
            if (shown >= MaxRows)
                continue;
            _list.AddChild(Row(item));
            shown++;
        }
        _count.Text = total == 0 ? "" : shown < total ? $"последние {shown} из {total}" : Fmt.Plural(total, "новость", "новости", "новостей");
        if (total == 0)
        {
            var empty = UiTheme.Text(_filter == "home" && _player == null
                ? "Вы ещё не выбрали страну: домашних новостей нет."
                : "Новостей пока нет. Запустите время — они появятся здесь.", 14, null, UiTheme.TextMuted);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _list.AddChild(empty);
        }
    }

    Control Row(NewsItem item)
    {
        var (tag, color, tone) = NewsTicker.Describe(_data, _names, item, _player);
        var normal = UiTheme.Box(new Color(1, 1, 1, 0), null, 8, 10, 6);
        var hover = UiTheme.Box(new Color(1, 1, 1, 0.07f), null, 8, 10, 6);
        var row = new PanelContainer { MouseFilter = MouseFilterEnum.Pass };
        row.AddThemeStyleboxOverride("panel", normal);
        var topic = _data.News.Topic(item.Topic)?.Name ?? "";
        row.TooltipText = item.Country == null ? topic : $"{topic}. Щелчок — показать страну на карте.";
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 10);
        row.AddChild(line);

        var date = UiTheme.Text(Fmt.Date(item.Date), 13, UiTheme.Numbers, UiTheme.TextMuted);
        date.CustomMinimumSize = new Vector2(132, 0);
        date.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        date.MouseFilter = MouseFilterEnum.Ignore;
        line.AddChild(date);
        var dot = new Panel { CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = SizeFlags.ShrinkBegin, MouseFilter = MouseFilterEnum.Ignore };
        dot.AddThemeStyleboxOverride("panel", UiTheme.Box(tone, null, 4, 0, 0));
        var dotBox = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        dotBox.AddThemeConstantOverride("margin_top", 6);
        dotBox.AddChild(dot);
        line.AddChild(dotBox);
        var label = UiTheme.Text(tag, 13, UiTheme.SemiBold, color);
        label.CustomMinimumSize = new Vector2(150, 0);
        label.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.MouseFilter = MouseFilterEnum.Ignore;
        line.AddChild(label);
        var text = UiTheme.Text(item.Text, 14, null, item.Home ? UiTheme.TextPrimary : UiTheme.TextSecondary);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.MouseFilter = MouseFilterEnum.Ignore;
        line.AddChild(text);

        if (item.Country is { } country)
        {
            row.MouseDefaultCursorShape = CursorShape.PointingHand;
            row.MouseEntered += () => row.AddThemeStyleboxOverride("panel", hover);
            row.MouseExited += () => row.AddThemeStyleboxOverride("panel", normal);
            row.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                {
                    CountryChosen?.Invoke(country);
                    row.AcceptEvent();
                }
            };
        }
        return row;
    }
}
