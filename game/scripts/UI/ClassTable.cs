using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Классы страны в досье: полоса долей населения и строка на каждый класс — доля, уровень жизни и удовлетворённость.
/// Подробности (доходы, интересы, радикализм, влияние) — во всплывающей подсказке строки.
/// </summary>
public partial class ClassTable : VBoxContainer
{
    IReadOnlyList<ClassRecord> _classes = null!;
    ClassShareBar _bar = null!;
    readonly List<(Control Row, Label Share, Label Living, Label Mood)> _rows = new();

    public void Setup(IReadOnlyList<ClassRecord> classes)
    {
        _classes = classes;
        AddThemeConstantOverride("separation", 1);
        _bar = new ClassShareBar();
        _bar.Setup(classes.Select(c => UiTheme.Hex(c.Color)).ToArray());
        AddChild(_bar);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        var caption = UiTheme.Text("", 11, null, UiTheme.TextMuted);
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(caption);
        header.AddChild(Column("доля", 11, UiTheme.TextMuted, 52));
        header.AddChild(Column("жизнь", 11, UiTheme.TextMuted, 44));
        header.AddChild(Column("настрой", 11, UiTheme.TextMuted, 56));
        AddChild(header);

        foreach (var c in classes)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop };
            row.AddThemeConstantOverride("separation", 6);
            var dot = new Panel { CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            dot.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Hex(c.Color), null, 4, 0, 0));
            dot.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(dot);
            var name = UiTheme.Text(c.Name, 13, null, UiTheme.TextSecondary);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.ClipText = true;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            row.AddChild(name);
            var share = Column("", 13, UiTheme.TextPrimary, 52);
            var living = Column("", 13, UiTheme.TextPrimary, 44);
            var mood = Column("", 13, UiTheme.TextPrimary, 56);
            row.AddChild(share);
            row.AddChild(living);
            row.AddChild(mood);
            AddChild(row);
            _rows.Add((row, share, living, mood));
        }
    }

    static Label Column(string text, int size, Color color, float width)
    {
        var label = UiTheme.Text(text, size, UiTheme.Numbers, color);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.CustomMinimumSize = new Vector2(width, 0);
        label.MouseFilter = MouseFilterEnum.Ignore;
        return label;
    }

    public void Show(IReadOnlyList<ClassSnapshot>? classes)
    {
        Visible = classes is { Count: > 0 };
        if (classes == null)
            return;
        _bar.Show(classes.Select(c => c.Share).ToArray(), _classes.Select(c => c.Name).ToArray());
        for (var k = 0; k < _rows.Count && k < classes.Count; k++)
        {
            var c = classes[k];
            var (row, share, living, mood) = _rows[k];
            share.Text = Fmt.Value("percent", c.Share);
            living.Text = Fmt.Number(c.StandardOfLiving, 0);
            mood.Text = Fmt.Number(c.Satisfaction, 0);
            mood.AddThemeColorOverride("font_color", MoodColor(c.Satisfaction));
            row.TooltipText = Tooltip(_classes[k], c);
        }
    }

    /// <summary>Красный — недовольство, золотой — тревога, обычный — спокойствие, зелёный — довольство.</summary>
    public static Color MoodColor(double satisfaction) => satisfaction switch
    {
        < 35 => UiTheme.Critical,
        < 45 => UiTheme.Accent,
        < 58 => UiTheme.TextPrimary,
        _ => Color.FromHtml("#7fc49a"),
    };

    static string Tooltip(ClassRecord r, ClassSnapshot c)
    {
        var lines = new List<string>
        {
            $"{r.Name}: {Fmt.Value("percent", c.Share)} населения ({Fmt.People(c.Size)} вместе с семьями)",
            "Источник дохода: " + r.Income.ToLowerInvariant(),
            $"Доход на человека: {Fmt.Value("money", c.Income)} в год, сбережения {Fmt.Value("money", c.Savings)}",
            $"Уровень жизни: {Fmt.Number(c.StandardOfLiving, 0)} из 100, ожидания {Fmt.Number(c.Expectations, 0)}",
            $"Удовлетворённость: {Fmt.Number(c.Satisfaction, 0)} из 100 (50 — спокойствие)",
            $"Радикализм: {Fmt.Number(c.Radicalism, 0)} из 100",
            $"Влияние на власть: {Fmt.Value("percent", c.Influence)}",
            "",
            "Интересы: " + string.Join(", ", r.Interests).ToLowerInvariant(),
        };
        return string.Join('\n', lines);
    }
}
