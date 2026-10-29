using Godot;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// График одного показателя по годам: линия 2 px с мягкой заливкой к основанию, три тихие линии сетки с подписями
/// слева, годы снизу. Последняя точка — текущее значение («сейчас»), к ней ведёт пунктир. Под курсором — вертикальная
/// линия, точка и подпись «год — значение» над графиком.
/// </summary>
public partial class LineChart : Control
{
    IReadOnlyList<(string Label, double Value)> _points = [];
    string _format = "number1";
    string? _unit;
    Color _color = UiTheme.Accent;
    bool _lastIsNow;
    int _hover = -1;

    const float Top = 18;
    const float Bottom = 18;
    const float Left = 52;
    const float Right = 8;
    const int FontSize = 11;

    public LineChart()
    {
        CustomMinimumSize = new Vector2(0, 120);
        // Pass, а не Stop: колесо мыши над графиком должно прокручивать экран.
        MouseFilter = MouseFilterEnum.Pass;
    }

    /// <summary>Points — подпись (год) и значение по порядку; LastIsNow — последняя точка не год, а текущее значение.</summary>
    public void Show(IReadOnlyList<(string Label, double Value)> points, string format, string? unit, Color color, bool lastIsNow)
    {
        (_points, _format, _unit, _color, _lastIsNow) = (points, format, unit, color, lastIsNow);
        _hover = -1;
        QueueRedraw();
    }

    (double Min, double Max) Range()
    {
        var min = _points.Min(p => p.Value);
        var max = _points.Max(p => p.Value);
        var pad = Math.Max((max - min) * 0.12, Math.Max(Math.Abs(max), 1) * 0.02);
        // Неотрицательные показатели не уходят ниже нуля, чтобы сетка не подписывалась «−1%».
        var floor = min >= 0 ? 0 : double.NegativeInfinity;
        return (Math.Max(floor, min - pad), max + pad);
    }

    Vector2 At(int i, double min, double max)
    {
        var width = Size.X - Left - Right;
        var x = Left + (_points.Count == 1 ? width / 2 : width * i / (_points.Count - 1));
        var y = Top + (Size.Y - Top - Bottom) * (float)(1 - (_points[i].Value - min) / (max - min));
        return new Vector2(x, y);
    }

    public override void _Draw()
    {
        if (_points.Count == 0)
        {
            DrawString(UiTheme.Regular, new Vector2(Left, Size.Y / 2), "Нет данных", HorizontalAlignment.Left, -1, 13, UiTheme.TextMuted);
            return;
        }
        var (min, max) = Range();
        var baseY = Size.Y - Bottom;
        var grid = new Color(1, 1, 1, 0.06f);
        for (var g = 0; g <= 2; g++)
        {
            var value = min + (max - min) * g / 2;
            var y = Top + (baseY - Top) * (1 - g / 2f);
            DrawLine(new Vector2(Left, y), new Vector2(Size.X - Right, y), grid, 1);
            var text = Fmt.Compact(_format, value);
            var w = UiTheme.Numbers.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
            DrawString(UiTheme.Numbers, new Vector2(Left - 8 - w, y + 4), text, HorizontalAlignment.Left, -1, FontSize, UiTheme.TextMuted);
        }

        var points = Enumerable.Range(0, _points.Count).Select(i => At(i, min, max)).ToArray();
        var top = new Color(_color, 0.22f);
        var bottom = new Color(_color, 0f);
        for (var i = 0; i + 1 < points.Length; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            DrawPolygon([a, b, new Vector2(b.X, baseY), new Vector2(a.X, baseY)], [top, top, bottom, bottom]);
        }
        var solid = _lastIsNow && points.Length > 1 ? points[..^1] : points;
        if (solid.Length > 1)
            DrawPolyline(solid, _color, 2, true);
        if (_lastIsNow && points.Length > 1)
            DrawDashedLine(points[^2], points[^1], _color, 2, 4, true, true);

        // Подписи годов: первый, середина и последний, чтобы не налезали друг на друга.
        var labels = new SortedSet<int> { 0, points.Length - 1 };
        if (points.Length > 4)
            labels.Add((points.Length - 1) / 2);
        foreach (var i in labels)
        {
            var text = _points[i].Label;
            var w = UiTheme.Numbers.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
            var x = Math.Clamp(points[i].X - w / 2, Left - 4, Size.X - w);
            DrawString(UiTheme.Numbers, new Vector2(x, Size.Y - 3), text, HorizontalAlignment.Left, -1, FontSize, UiTheme.TextMuted);
        }

        // Последняя точка выделена всегда: это то, что сейчас.
        DrawCircle(points[^1], 5, UiTheme.PanelRaised);
        DrawCircle(points[^1], 4, _color);

        if (_hover >= 0 && _hover < points.Length)
        {
            var p = points[_hover];
            DrawLine(new Vector2(p.X, Top - 4), new Vector2(p.X, baseY), new Color(1, 1, 1, 0.25f), 1);
            DrawCircle(p, 6, UiTheme.PanelRaised);
            DrawCircle(p, 4.5f, _color);
            var text = $"{_points[_hover].Label}: {Fmt.Value(_format, _points[_hover].Value, _unit)}";
            var w = UiTheme.Medium.GetStringSize(text, HorizontalAlignment.Left, -1, 12).X;
            var x = Math.Clamp(p.X - w / 2, Left, Size.X - Right - w);
            DrawString(UiTheme.Medium, new Vector2(x, 12), text, HorizontalAlignment.Left, -1, 12, UiTheme.TextPrimary);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseMotion motion || _points.Count == 0)
            return;
        var hover = Nearest(motion.Position.X);
        if (hover == _hover)
            return;
        _hover = hover;
        QueueRedraw();
    }

    int Nearest(float x)
    {
        if (_points.Count == 1)
            return 0;
        var width = Size.X - Left - Right;
        return Math.Clamp((int)Math.Round((x - Left) / width * (_points.Count - 1)), 0, _points.Count - 1);
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
