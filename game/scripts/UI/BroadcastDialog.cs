using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.News;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Телевыпуск «Неделя»: слева — студия с ведущим (портрет-заглушка), справа — текущая страница выпуска: приветствие
/// со списком сюжетов, по сюжету на страницу, прощание. Кнопка «Дальше» (или Enter, пробел, стрелка вправо) листает
/// выпуск. Текст на экране — тот же, что прочтёт голос на этапе 8.
/// </summary>
public partial class BroadcastDialog : ModalDialog
{
    GameData _data = null!;
    Dictionary<string, string> _names = null!;
    Broadcast? _show;
    int _page;
    Portrait _portrait = null!;
    Label _anchorName = null!;
    Label _anchorRole = null!;
    VBoxContainer _pageBox = null!;
    HBoxContainer _dots = null!;

    /// <summary>Щелчок «Показать на карте» у мирового сюжета: страна сюжета.</summary>
    public event Action<string>? CountryChosen;

    /// <summary>Какой выпуск открыт и на какой странице (0 — приветствие, последняя — прощание).</summary>
    public int ShownId => _show?.Id ?? 0;
    public int Page => _page;
    public int Pages => _show == null ? 0 : _show.Stories.Count + 2;

    public void Setup(GameData data)
    {
        _data = data;
        _names = data.Countries.Countries.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        InitDialog(860);

        var layout = new HBoxContainer();
        layout.AddThemeConstantOverride("separation", 22);
        Content.AddChild(layout);

        // Студия: тёмно-синий экран с ведущим и плашкой эфира.
        var studio = new PanelContainer { CustomMinimumSize = new Vector2(230, 0), SizeFlagsVertical = SizeFlags.ShrinkBegin };
        studio.AddThemeStyleboxOverride("panel", UiTheme.Box(Color.FromHtml("#121b2a"), Color.FromHtml("#5a8cc84d"), 12, 16, 14));
        layout.AddChild(studio);
        var desk = new VBoxContainer();
        desk.AddThemeConstantOverride("separation", 8);
        studio.AddChild(desk);
        var onAir = new HBoxContainer();
        onAir.AddThemeConstantOverride("separation", 6);
        var red = new Panel { CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        red.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Critical, null, 4, 0, 0));
        onAir.AddChild(red);
        onAir.AddChild(UiTheme.Text("В ЭФИРЕ", 11, UiTheme.SemiBold, UiTheme.Critical));
        desk.AddChild(onAir);
        var frame = new CenterContainer();
        desk.AddChild(frame);
        _portrait = new Portrait { CustomMinimumSize = new Vector2(168, 218) };
        frame.AddChild(_portrait);
        _anchorName = UiTheme.Text("", 16, UiTheme.SemiBold, UiTheme.TextPrimary);
        _anchorName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _anchorName.HorizontalAlignment = HorizontalAlignment.Center;
        desk.AddChild(_anchorName);
        _anchorRole = UiTheme.Text("", 12, null, UiTheme.TextMuted);
        _anchorRole.HorizontalAlignment = HorizontalAlignment.Center;
        _anchorRole.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desk.AddChild(_anchorRole);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 300) };
        right.AddThemeConstantOverride("separation", 10);
        layout.AddChild(right);
        _pageBox = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _pageBox.AddThemeConstantOverride("separation", 10);
        right.AddChild(_pageBox);
        _dots = new HBoxContainer();
        _dots.AddThemeConstantOverride("separation", 6);
        right.AddChild(_dots);
    }

    public void Show(Broadcast show)
    {
        _show = show;
        _page = 0;
        Title.Text = Fmt.Quote(show.Show);
        Subtitle.Text = $"Телевыпуск {Fmt.Date(show.Date)} · {Fmt.Plural(show.Stories.Count, "сюжет", "сюжета", "сюжетов")}";
        Subtitle.Visible = true;
        var anchor = show.Anchor;
        _portrait.Show(anchor.Female, anchor.Age(show.Date), anchor.Number * 7 + show.Country.Sum(c => c));
        _anchorName.Text = anchor.Name;
        _anchorRole.Text = (anchor.Female ? "ведущая" : "ведущий") + (show.Debut ? " · первый выпуск" : $" · в эфире с {anchor.Since.Year} года");
        Render();
        Visible = true;
    }

    /// <summary>Следующая страница; с последней — закрыть выпуск.</summary>
    public void Next()
    {
        if (_show == null)
            return;
        if (_page >= Pages - 1)
        {
            Visible = false;
            return;
        }
        _page++;
        Render();
    }

    public void Previous()
    {
        if (_show == null || _page == 0)
            return;
        _page--;
        Render();
    }

    void Render()
    {
        var show = _show!;
        foreach (var child in _pageBox.GetChildren())
        {
            _pageBox.RemoveChild(child);
            child.QueueFree();
        }
        if (_page == 0)
        {
            _pageBox.AddChild(Kicker("Начало выпуска"));
            _pageBox.AddChild(Line(show.Opening, 17, UiTheme.TextPrimary));
            _pageBox.AddChild(new HSeparator());
            _pageBox.AddChild(Kicker("В выпуске"));
            foreach (var story in show.Stories)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 10);
                row.AddChild(ToneDot(story.Tone));
                var (tag, color) = Tag(story, show.Country);
                var label = UiTheme.Text(tag, 13, UiTheme.SemiBold, color);
                label.CustomMinimumSize = new Vector2(130, 0);
                label.ClipText = true;
                label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                row.AddChild(label);
                var text = Line(story.Headline, 14, UiTheme.TextSecondary);
                text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                row.AddChild(text);
                _pageBox.AddChild(row);
            }
        }
        else if (_page <= show.Stories.Count)
        {
            var story = show.Stories[_page - 1];
            _pageBox.AddChild(Kicker($"Сюжет {_page} из {show.Stories.Count}"));
            if (story.Lead.Length > 0)
                _pageBox.AddChild(Line("— " + story.Lead, 14, UiTheme.TextMuted));
            var head = new HBoxContainer();
            head.AddThemeConstantOverride("separation", 10);
            head.AddChild(ToneDot(story.Tone));
            var (tag, color) = Tag(story, show.Country);
            head.AddChild(UiTheme.Text(tag, 14, UiTheme.SemiBold, color));
            if (_data.News.Topic(story.Topic) is { } topic)
                head.AddChild(UiTheme.Text("· " + topic.Name, 13, null, UiTheme.TextMuted));
            _pageBox.AddChild(head);
            var stripe = new ColorRect
            {
                Color = _data.News.Topic(story.Topic) is { } t ? UiTheme.Hex(t.Color) : UiTheme.Accent, CustomMinimumSize = new Vector2(48, 3),
                SizeFlagsHorizontal = SizeFlags.ShrinkBegin, MouseFilter = MouseFilterEnum.Ignore,
            };
            _pageBox.AddChild(stripe);
            _pageBox.AddChild(Line(story.Headline, 24, UiTheme.TextPrimary, UiTheme.SerifBold));
            if (!story.Home && story.Country is { } country && country != show.Country)
            {
                var map = new Button { Text = "Показать на карте", Flat = true, FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
                map.AddThemeColorOverride("font_color", UiTheme.Accent);
                map.AddThemeFontSizeOverride("font_size", 13);
                map.Pressed += () =>
                {
                    Visible = false;
                    CountryChosen?.Invoke(country);
                };
                _pageBox.AddChild(map);
            }
        }
        else
        {
            _pageBox.AddChild(Kicker("Конец выпуска"));
            _pageBox.AddChild(Line(show.Closing, 17, UiTheme.TextPrimary));
            _pageBox.AddChild(Line("Прошлые выпуски — в списке новостей (J), вкладка «Выпуски». Показывать выпуск окном или только в ленте — там же.",
                12, UiTheme.TextMuted));
        }

        foreach (var child in _dots.GetChildren())
        {
            _dots.RemoveChild(child);
            child.QueueFree();
        }
        for (var k = 0; k < Pages; k++)
        {
            var dot = new Panel { CustomMinimumSize = new Vector2(k == _page ? 18 : 7, 7), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
            dot.AddThemeStyleboxOverride("panel", UiTheme.Box(k == _page ? UiTheme.Accent : k < _page ? UiTheme.TextMuted : UiTheme.ButtonHover, null, 4, 0, 0));
            _dots.AddChild(dot);
        }

        ClearButtons();
        var last = _page >= Pages - 1;
        if (!last)
            AddButton("Закрыть выпуск", false, () => Visible = false);
        if (_page > 0)
            AddButton("Назад", false, Previous);
        AddButton(last ? "Закрыть" : "Дальше", true, Next);
    }

    (string Tag, Color Color) Tag(BroadcastStory story, string country)
    {
        var tag = story.Home ? "Дома" : story.Country == null ? (_data.News.Broadcast?.Markets ?? "Мир") : _names.GetValueOrDefault(story.Country, story.Country);
        var color = _data.News.Topic(story.Topic) is { } topic ? UiTheme.Hex(topic.Color) : UiTheme.TextSecondary;
        return (tag, color);
    }

    static Label Kicker(string text) => UiTheme.Text(text.ToUpperInvariant(), 11, UiTheme.SemiBold, UiTheme.Accent);

    static readonly Color Good = Color.FromHtml("#7fb069");

    static Control ToneDot(string tone)
    {
        var dot = new Panel { CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        dot.AddThemeStyleboxOverride("panel", UiTheme.Box(tone switch { "good" => Good, "bad" => UiTheme.Critical, _ => UiTheme.TextMuted }, null, 4, 0, 0));
        return dot;
    }
}
