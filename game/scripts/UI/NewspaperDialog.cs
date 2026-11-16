using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.News;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Газета: первая полоса номера на «бумаге» — шапка с названием и девизом издания, заголовок месяца крупно и три
/// материала колонками. Шрифт — PT Serif. Кнопки листают номера за год (стрелки влево и вправо — тоже).
/// Сама газета не всплывает: её открывают кнопкой «Газета» на ленте или клавишей K.
/// </summary>
public partial class NewspaperDialog : Control
{
    static readonly Color Paper = Color.FromHtml("#efe8d6");
    static readonly Color Ink = Color.FromHtml("#1f1d1a");
    static readonly Color Faded = Color.FromHtml("#655e52");
    static readonly Color Rubric = Color.FromHtml("#9b2a1f");
    static readonly Color Rule = Color.FromHtml("#1f1d1a59");

    GameData _data = null!;
    Dictionary<string, string> _names = null!;
    IReadOnlyList<Newspaper> _papers = [];
    int _index;
    string? _country;
    VBoxContainer _page = null!;
    Button _older = null!;
    Button _newer = null!;
    Label _counter = null!;

    /// <summary>Открытый номер (0 — газета закрыта).</summary>
    public int ShownId => Visible && _index < _papers.Count ? _papers[_index].Id : 0;

    public void Setup(GameData data)
    {
        _data = data;
        _names = data.Countries.Countries.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.04f, 0.7f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        center.AddChild(column);

        var sheet = new PanelContainer { CustomMinimumSize = new Vector2(940, 0) };
        sheet.AddThemeStyleboxOverride("panel", UiTheme.Box(Paper, Color.FromHtml("#00000040"), 3, 36, 26));
        column.AddChild(sheet);
        _page = new VBoxContainer();
        _page.AddThemeConstantOverride("separation", 10);
        sheet.AddChild(_page);

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 10);
        column.AddChild(bar);
        _older = Button("← Прошлый номер", () => Turn(-1));
        bar.AddChild(_older);
        _counter = UiTheme.Text("", 13, null, UiTheme.TextSecondary);
        _counter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _counter.HorizontalAlignment = HorizontalAlignment.Center;
        bar.AddChild(_counter);
        _newer = Button("Следующий номер →", () => Turn(1));
        bar.AddChild(_newer);
        bar.AddChild(Button("Закрыть", () => Visible = false));
    }

    static Button Button(string text, Action pressed)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) };
        b.Pressed += pressed;
        return b;
    }

    /// <summary>Открыть газету страны на номере id (0 — последний).</summary>
    public void Show(IReadOnlyList<Newspaper> papers, string country, int id = 0)
    {
        _papers = papers;
        _country = country;
        if (papers.Count == 0)
            return;
        _index = id == 0 ? papers.Count - 1 : Math.Max(0, papers.ToList().FindIndex(p => p.Id == id));
        Render();
        Visible = true;
    }

    /// <summary>Листать номера: −1 — прошлый, +1 — следующий.</summary>
    public void Turn(int step)
    {
        var next = Math.Clamp(_index + step, 0, _papers.Count - 1);
        if (next == _index)
            return;
        _index = next;
        Render();
    }

    void Render()
    {
        foreach (var child in _page.GetChildren())
        {
            _page.RemoveChild(child);
            child.QueueFree();
        }
        var paper = _papers[_index];

        var meta = Text($"№ {paper.Id}  ·  {Fmt.Date(paper.Date)}  ·  {paper.Motto}", 13, UiTheme.Serif, Faded);
        meta.HorizontalAlignment = HorizontalAlignment.Center;
        _page.AddChild(meta);
        var name = Text(paper.Name.ToUpperInvariant(), 46, UiTheme.SerifBold, Ink);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        _page.AddChild(name);
        _page.AddChild(Line(3));
        _page.AddChild(Line(1));

        _page.AddChild(Text(Kicker(paper.Lead), 13, UiTheme.SemiBold, Rubric));
        _page.AddChild(Text(paper.Lead.Headline, 32, UiTheme.SerifBold, Ink));
        _page.AddChild(Text(paper.Lead.Body, 17, UiTheme.Serif, Ink));
        _page.AddChild(Line(1));

        if (paper.Stories.Count > 0)
        {
            var columns = new HBoxContainer();
            columns.AddThemeConstantOverride("separation", 18);
            _page.AddChild(columns);
            for (var k = 0; k < paper.Stories.Count; k++)
            {
                if (k > 0)
                    columns.AddChild(new ColorRect { Color = Rule, CustomMinimumSize = new Vector2(1, 0), MouseFilter = MouseFilterEnum.Ignore });
                var story = paper.Stories[k];
                var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1 };
                col.AddThemeConstantOverride("separation", 6);
                col.CustomMinimumSize = new Vector2(250, 0);
                columns.AddChild(col);
                col.AddChild(Text(Kicker(story), 12, UiTheme.SemiBold, Rubric));
                col.AddChild(Text(story.Headline, 19, UiTheme.SerifBold, Ink));
                col.AddChild(Text(story.Body, 14, UiTheme.Serif, Faded));
            }
        }
        _older.Disabled = _index == 0;
        _newer.Disabled = _index >= _papers.Count - 1;
        _counter.Text = $"Номер {_index + 1} из {_papers.Count} за последний год · листать — стрелками ← →";
    }

    /// <summary>Рубрика над заголовком: «Экономика», «В мире · Германия», «Расследование»…</summary>
    string Kicker(PaperStory story)
    {
        var topic = _data.News.Topic(story.Topic)?.Name ?? "";
        return (story.Kind switch
        {
            "expose" => "Расследование",
            "praise" => "Официально",
            "quiet" => "Хроника",
            _ when story.Country == null => "Мировые рынки",
            _ when story.Country != _country => $"В мире · {_names.GetValueOrDefault(story.Country, story.Country)}",
            _ => topic,
        }).ToUpperInvariant();
    }

    static Label Text(string text, int size, Font font, Color color)
    {
        var label = UiTheme.Text(text, size, font, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    static ColorRect Line(int height) =>
        new() { Color = Ink, CustomMinimumSize = new Vector2(0, height), MouseFilter = MouseFilterEnum.Ignore };
}
