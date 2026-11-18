using Godot;
using Politeconomy.Sim.Data;

namespace Politeconomy.Game.UI;

/// <summary>
/// Одна ось взглядов: подписи краёв («перераспределение» — «рынок»), линия с отметкой середины и точка позиции.
/// Вторая, полупрозрачная точка — средние взгляды страны (для сравнения).
/// </summary>
public partial class AxisScale : Control
{
    string _low = "";
    string _high = "";
    double _value;
    double? _mean;
    Color _color = UiTheme.Accent;
    const int FontSize = 10;
    const float LabelWidth = 108;

    public AxisScale()
    {
        CustomMinimumSize = new Vector2(0, 16);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(AxisRecord axis, double value, Color color, double? mean = null)
    {
        (_low, _high, _value, _color, _mean) = (axis.Low, axis.High, value, color, mean);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = UiTheme.Regular;
        var lowWidth = font.GetStringSize(_low, HorizontalAlignment.Left, -1, FontSize).X;
        DrawString(font, new Vector2(LabelWidth - lowWidth, 12), _low, HorizontalAlignment.Left, -1, FontSize, UiTheme.TextMuted);
        DrawString(font, new Vector2(Size.X - LabelWidth + 6, 12), _high, HorizontalAlignment.Left, LabelWidth - 6, FontSize, UiTheme.TextMuted);
        var left = LabelWidth + 8;
        var right = Size.X - LabelWidth;
        const float y = 8;
        DrawLine(new Vector2(left, y), new Vector2(right, y), new Color(1, 1, 1, 0.12f), 2, true);
        var middle = (left + right) / 2;
        DrawLine(new Vector2(middle, y - 4), new Vector2(middle, y + 4), new Color(1, 1, 1, 0.25f), 1);
        float X(double v) => left + (right - left) * (float)((Math.Clamp(v, -100, 100) + 100) / 200);
        if (_mean is { } m)
            DrawCircle(new Vector2(X(m), y), 4, new Color(1, 1, 1, 0.22f));
        DrawCircle(new Vector2(X(_value), y), 5.5f, UiTheme.PanelRaised);
        DrawCircle(new Vector2(X(_value), y), 4.5f, _color);
    }
}
