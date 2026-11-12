using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.News;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Лента новостей внизу экрана: одна строка — метка «Дома» или страна, рубрика цветом, текст и дата. Новые новости
/// сменяют друг друга (важные — первыми), в затишье лента листает последние. Щелчок раскрывает список всех новостей.
/// </summary>
public partial class NewsTicker : GlassPanel
{
    /// <summary>Сколько секунд новость держится, прежде чем её сменит следующая, и через сколько лента начинает листать старые.</summary>
    const double MinShow = 3.5;
    const double IdleShow = 7;
    const int Replay = 6;

    GameData _data = null!;
    Dictionary<string, string> _names = null!;
    HBoxContainer _row = null!;
    Panel _dot = null!;
    Label _tag = null!;
    Label _text = null!;
    Label _date = null!;
    Button _all = null!;
    Tween? _fade;

    IReadOnlyList<NewsItem> _items = [];
    string? _player;
    readonly List<NewsItem> _queue = [];
    NewsItem? _shown;
    int _lastId;
    int _version = -1;
    int _replay;
    double _since;
    bool _first = true;

    /// <summary>Щелчок по ленте: раскрыть или свернуть список новостей.</summary>
    public event Action? Toggled;

    /// <summary>Новость, которая сейчас на ленте.</summary>
    public NewsItem? Current => _shown;

    public void Setup(GameData data)
    {
        _data = data;
        _names = data.Countries.Countries.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        SetAnchorsPreset(LayoutPreset.BottomWide);
        OffsetLeft = 494;
        OffsetRight = -12;
        OffsetTop = -58;
        OffsetBottom = -12;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "Лента новостей: дома и в мире. Щелчок — все новости (J).";
        InitGlass(radius: 12, padH: 16, padV: 8, tint: 0.86f);

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 12);
        Body.AddChild(line);
        var title = UiTheme.Text("НОВОСТИ", 12, UiTheme.SemiBold, UiTheme.Accent);
        title.MouseFilter = MouseFilterEnum.Ignore;
        line.AddChild(title);
        line.AddChild(new VSeparator { MouseFilter = MouseFilterEnum.Ignore });

        _row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        _row.AddThemeConstantOverride("separation", 10);
        line.AddChild(_row);
        _dot = new Panel { CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        _row.AddChild(_dot);
        _tag = UiTheme.Text("", 13, UiTheme.SemiBold);
        _tag.MouseFilter = MouseFilterEnum.Ignore;
        _row.AddChild(_tag);
        _text = UiTheme.Text("", 15, null, UiTheme.TextPrimary);
        _text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _text.ClipText = true;
        _text.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _text.MouseFilter = MouseFilterEnum.Ignore;
        _row.AddChild(_text);
        _date = UiTheme.Text("", 13, UiTheme.Numbers, UiTheme.TextMuted);
        _date.MouseFilter = MouseFilterEnum.Ignore;
        _row.AddChild(_date);

        _all = new Button { Text = "Все новости", FocusMode = FocusModeEnum.None, ToggleMode = true, TooltipText = "Список новостей (J)" };
        _all.AddThemeFontSizeOverride("font_size", 13);
        _all.Pressed += () => Toggled?.Invoke();
        line.AddChild(_all);

        GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Toggled?.Invoke();
                AcceptEvent();
            }
        };
        Display(null);
    }

    /// <summary>Отметить кнопку «Все новости», когда список раскрыт.</summary>
    public void SetOpen(bool open) => _all.SetPressedNoSignal(open);

    /// <summary>Свежий снимок ленты: новые новости встают в очередь на показ.</summary>
    public void Show(NewsSnapshot news, string? player)
    {
        if (news.Version == _version && player == _player)
            return;
        _version = news.Version;
        _player = player;
        _items = news.Items;
        if (_first || (_items.Count > 0 && _items[^1].Id < _lastId))
        {
            // Первый снимок или загруженная партия: показываем последнюю новость, старые в очередь не ставим.
            _first = false;
            _queue.Clear();
            _lastId = _items.Count > 0 ? _items[^1].Id : 0;
            Display(_items.Count > 0 ? _items[^1] : null);
            return;
        }
        foreach (var item in _items)
            if (item.Id > _lastId)
                _queue.Add(item);
        if (_items.Count > 0)
            _lastId = Math.Max(_lastId, _items[^1].Id);
        if (_shown == null && _queue.Count > 0)
            Next();
    }

    public override void _Process(double delta)
    {
        _since += delta;
        if (_queue.Count > 0)
        {
            if (_since >= MinShow)
                Next();
        }
        else if (_since >= IdleShow && _items.Count > 1)
        {
            // Затишье: листаем последние новости от свежих к старым.
            _replay = (_replay + 1) % Math.Min(Replay, _items.Count);
            Display(_items[_items.Count - 1 - _replay], replay: true);
        }
    }

    /// <summary>Следующая из очереди — самая важная; всё, что старше неё, на ленту уже не попадёт (оно есть в списке).</summary>
    void Next()
    {
        var best = _queue[0];
        foreach (var item in _queue)
            if (item.Weight > best.Weight)
                best = item;
        _queue.RemoveAll(i => i.Id <= best.Id);
        _replay = 0;
        Display(best);
    }

    void Display(NewsItem? item, bool replay = false)
    {
        _since = 0;
        _shown = item;
        if (item == null)
        {
            _dot.Visible = false;
            _tag.Text = "";
            _text.Text = "Пока тихо. Запустите время — новости дома и в мире появятся здесь.";
            _text.AddThemeColorOverride("font_color", UiTheme.TextMuted);
            _date.Text = "";
            return;
        }
        var (tag, color, tone) = Describe(_data, _names, item, _player);
        _dot.Visible = true;
        _dot.AddThemeStyleboxOverride("panel", UiTheme.Box(tone, null, 4, 0, 0));
        _tag.Text = tag;
        _tag.AddThemeColorOverride("font_color", color);
        _text.Text = item.Text;
        _text.AddThemeColorOverride("font_color", UiTheme.TextPrimary);
        _date.Text = Fmt.Date(item.Date);
        _fade?.Kill();
        _row.Modulate = new Color(1, 1, 1, replay ? 0.35f : 0);
        _fade = CreateTween();
        _fade.TweenProperty(_row, "modulate:a", 1f, 0.35f);
    }

    static readonly Color Good = Color.FromHtml("#7fb069");

    /// <summary>
    /// Как подписать новость: метка («Дома», страна или «Мир»), цвет рубрики и цвет тона (хорошая — зелёный, плохая —
    /// красный, нейтральная — серый).
    /// </summary>
    public static (string Tag, Color Color, Color Tone) Describe(GameData data, Dictionary<string, string> names, NewsItem item, string? player)
    {
        var tag = item.Country == null ? "Мир" : item.Country == player ? "Дома" : names.GetValueOrDefault(item.Country, item.Country);
        var color = data.News.Topic(item.Topic) is { } topic ? UiTheme.Hex(topic.Color) : UiTheme.TextSecondary;
        var tone = item.Tone switch { "good" => Good, "bad" => UiTheme.Critical, _ => UiTheme.TextMuted };
        return (tag, color, tone);
    }
}
