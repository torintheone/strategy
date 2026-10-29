using Godot;

namespace Politeconomy.Game.UI;

/// <summary>Тонкая полоска качества 0–100 с подписью слева и числом справа.</summary>
public partial class Meter : Control
{
    string _label = "";
    double _value;
    Color _color = UiTheme.Accent;

    public Meter()
    {
        CustomMinimumSize = new Vector2(0, 16);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(string label, double value, Color color)
    {
        (_label, _value, _color) = (label, value, color);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = UiTheme.Regular;
        const int fontSize = 11;
        DrawString(font, new Vector2(0, 11), _label, HorizontalAlignment.Left, -1, fontSize, UiTheme.TextMuted);
        var number = Math.Round(_value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var w = font.GetStringSize(number, HorizontalAlignment.Left, -1, fontSize).X;
        DrawString(UiTheme.Numbers, new Vector2(Size.X - w, 11), number, HorizontalAlignment.Left, -1, fontSize, UiTheme.TextSecondary);
        var left = Size.X * 0.48f;
        var right = Size.X - 26;
        var y = 7f;
        DrawLine(new Vector2(left, y), new Vector2(right, y), new Color(1, 1, 1, 0.08f), 4, true);
        var x = left + (right - left) * (float)Math.Clamp(_value / 100, 0, 1);
        DrawLine(new Vector2(left, y), new Vector2(x, y), _color, 4, true);
    }
}
