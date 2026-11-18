using Godot;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Опросы по месяцам: линия 2 px на каждую партию её цветом, сетка через 10%, справа у конца каждой линии —
/// короткое название и последнее значение (подписи раздвигаются, чтобы не налезать). Под курсором — вертикальная
/// линия и табличка со всеми партиями в этом месяце.
/// </summary>
public partial class PollChart : Control
{
    public sealed record Series(string Label, Color Color, IReadOnlyList<double> Values);

    IReadOnlyList<string> _dates = [];
    IReadOnlyList<Series> _series = [];
    int _hover = -1;

    const float Top = 10;
    const float Bottom = 20;
    const float Left = 40;
    const float Right = 128;
    const int FontSize = 11;

    public PollChart()
    {
        CustomMinimumSize = new Vector2(0, 220);
        MouseFilter = MouseFilterEnum.Pass;
    }

    /// <summary>Dates — подписи месяцев; у каждой серии значения 0–1 по тем же месяцам (NaN — партии ещё не было).</summary>
    public void Show(IReadOnlyList<string> dates, IReadOnlyList<Series> series)
    {
        (_dates, _series) = (dates, series);
        _hover = -1;
        QueueRedraw();
    }

    double Max()
    {
        var max = _series.SelectMany(s => s.Values).Where(double.IsFinite).DefaultIfEmpty(0.1).Max();
        return Math.Clamp(Math.Ceiling(max * 10 + 0.3) / 10, 0.2, 1);
    }

    float X(int i) => Left + (_dates.Count <= 1 ? (Size.X - Left - Right) / 2 : (Size.X - Left - Right) * i / (_dates.Count - 1));

    float Y(double v, double max) => Top + (Size.Y - Top - Bottom) * (float)(1 - v / max);

    public override void _Draw()
    {
        if (_dates.Count == 0 || _series.Count == 0)
        {
            DrawString(UiTheme.Regular, new Vector2(Left, Size.Y / 2), "Опросов пока нет", HorizontalAlignment.Left, -1, 13, UiTheme.TextMuted);
            return;
        }
        var max = Max();
        var grid = new Color(1, 1, 1, 0.06f);
        for (var g = 0.0; g <= max + 1e-9; g += 0.1)
        {
            var y = Y(g, max);
            DrawLine(new Vector2(Left, y), new Vector2(Size.X - Right + 8, y), grid, 1);
            var text = Fmt.Number(g * 100, 0) + "%";
            var w = UiTheme.Numbers.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
            DrawString(UiTheme.Numbers, new Vector2(Left - 8 - w, y + 4), text, HorizontalAlignment.Left, -1, FontSize, UiTheme.TextMuted);
        }

        foreach (var s in _series)
        {
            var points = Enumerable.Range(0, Math.Min(_dates.Count, s.Values.Count))
                .Where(i => double.IsFinite(s.Values[i]))
                .Select(i => new Vector2(X(i), Y(s.Values[i], max)))
                .ToArray();
            if (points.Length > 1)
                DrawPolyline(points, s.Color, 2, true);
            if (points.Length > 0)
                DrawCircle(points[^1], 3.5f, s.Color);
        }

        // Подписи справа: по последнему значению, раздвинутые по вертикали не меньше чем на 14 px.
        var labels = _series
            .Select(s => (s, Value: s.Values.LastOrDefault(double.IsFinite, double.NaN)))
            .Where(x => double.IsFinite(x.Value))
            .OrderByDescending(x => x.Value)
            .Select(x => (x.s, x.Value, Y: Y(x.Value, max)))
            .ToList();
        var placed = new List<float>();
        foreach (var (s, value, y0) in labels)
        {
            var y = placed.Count > 0 ? Math.Max(y0, placed[^1] + 14) : y0;
            placed.Add(y);
            var x = Size.X - Right + 14;
            DrawCircle(new Vector2(x, y), 3.5f, s.Color);
            DrawString(UiTheme.Medium, new Vector2(x + 8, y + 4), $"{Short(s.Label)} {Fmt.Number(value * 100, 0)}%", HorizontalAlignment.Left,
                Right - 26, FontSize, UiTheme.TextSecondary);
        }

        var marks = new SortedSet<int> { 0, _dates.Count - 1 };
        if (_dates.Count > 6)
            marks.Add((_dates.Count - 1) / 2);
        foreach (var i in marks)
        {
            var text = _dates[i];
            var w = UiTheme.Numbers.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
            var x = Math.Clamp(X(i) - w / 2, Left - 4, Size.X - Right - w);
            DrawString(UiTheme.Numbers, new Vector2(x, Size.Y - 4), text, HorizontalAlignment.Left, -1, FontSize, UiTheme.TextMuted);
        }

        if (_hover >= 0 && _hover < _dates.Count)
            DrawHover(_hover, max);
    }

    static string Short(string label) => label.Length > 12 ? label[..11] + "…" : label;

    void DrawHover(int i, double max)
    {
        var x = X(i);
        DrawLine(new Vector2(x, Top), new Vector2(x, Size.Y - Bottom), new Color(1, 1, 1, 0.25f), 1);
        var rows = _series.Where(s => i < s.Values.Count && double.IsFinite(s.Values[i])).OrderByDescending(s => s.Values[i]).ToList();
        foreach (var s in rows)
        {
            var y = Y(s.Values[i], max);
            DrawCircle(new Vector2(x, y), 5, UiTheme.PanelRaised);
            DrawCircle(new Vector2(x, y), 3.5f, s.Color);
        }
        const float lineHeight = 16;
        var width = 170f;
        var height = 10 + lineHeight * (rows.Count + 1);
        var left = x + 12 + width > Size.X ? x - 12 - width : x + 12;
        var box = new Rect2(left, Top, width, height);
        DrawStyleBox(UiTheme.Box(UiTheme.PanelRaised, UiTheme.PanelBorder, 8, 0, 0), box);
        DrawString(UiTheme.Medium, new Vector2(left + 10, Top + 16), _dates[i], HorizontalAlignment.Left, -1, 12, UiTheme.TextPrimary);
        for (var r = 0; r < rows.Count; r++)
        {
            var y = Top + 16 + lineHeight * (r + 1);
            DrawCircle(new Vector2(left + 14, y - 4), 3.5f, rows[r].Color);
            DrawString(UiTheme.Regular, new Vector2(left + 24, y), Short(rows[r].Label), HorizontalAlignment.Left, 100, 12, UiTheme.TextSecondary);
            var value = Fmt.Number(rows[r].Values[i] * 100, 1) + "%";
            var w = UiTheme.Numbers.GetStringSize(value, HorizontalAlignment.Left, -1, 12).X;
            DrawString(UiTheme.Numbers, new Vector2(left + width - 10 - w, y), value, HorizontalAlignment.Left, -1, 12, UiTheme.TextPrimary);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseMotion motion || _dates.Count == 0)
            return;
        var width = Size.X - Left - Right;
        var i = _dates.Count == 1 ? 0 : Math.Clamp((int)Math.Round((motion.Position.X - Left) / width * (_dates.Count - 1)), 0, _dates.Count - 1);
        if (motion.Position.X > Size.X - Right + 8)
            i = -1;
        if (i == _hover)
            return;
        _hover = i;
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationMouseExit && _hover >= 0)
        {
            _hover = -1;
            QueueRedraw();
        }
    }
}
