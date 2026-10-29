using Godot;

namespace Politeconomy.Game.UI;

/// <summary>Круглая кнопка экрана в левой колонке: значок рисуется линиями, подпись — во всплывающей подсказке.</summary>
public partial class RailButton : Button
{
    public string Glyph { get; set; } = "";

    public RailButton()
    {
        CustomMinimumSize = new Vector2(44, 44);
        FocusMode = FocusModeEnum.None;
        ToggleMode = true;
    }

    public override void _Draw()
    {
        var color = ButtonPressed ? UiTheme.Accent : IsHovered() ? UiTheme.TextPrimary : UiTheme.TextSecondary;
        var c = Size / 2;
        const float w = 1.6f;
        switch (Glyph)
        {
            case "government":
                // Здание с колоннами: крыша, три колонны, основание.
                DrawPolyline([c + new Vector2(-11, -4), c + new Vector2(0, -11), c + new Vector2(11, -4), c + new Vector2(-11, -4)], color, w, true);
                for (var i = -1; i <= 1; i++)
                    DrawLine(c + new Vector2(i * 7, -1), c + new Vector2(i * 7, 8), color, w + 0.6f, true);
                DrawLine(c + new Vector2(-12, 11), c + new Vector2(12, 11), color, w, true);
                break;
            case "ministries":
                // Портфель: корпус, ручка, полоса с застёжкой.
                DrawPolyline([c + new Vector2(-11, -5), c + new Vector2(11, -5), c + new Vector2(11, 10), c + new Vector2(-11, 10),
                    c + new Vector2(-11, -5)], color, w, true);
                DrawPolyline([c + new Vector2(-4, -5), c + new Vector2(-4, -9), c + new Vector2(4, -9), c + new Vector2(4, -5)], color, w, true);
                DrawLine(c + new Vector2(-11, 1), c + new Vector2(11, 1), color, w, true);
                DrawRect(new Rect2(c + new Vector2(-2.5f, -0.5f), new Vector2(5, 4)), color);
                break;
            case "politics":
                // Урна для голосования: ящик с прорезью и бюллетень над ней.
                DrawPolyline([c + new Vector2(-11, -2), c + new Vector2(11, -2), c + new Vector2(11, 11), c + new Vector2(-11, 11),
                    c + new Vector2(-11, -2)], color, w, true);
                DrawLine(c + new Vector2(-5, 3), c + new Vector2(5, 3), color, w + 0.6f, true);
                DrawPolyline([c + new Vector2(-4, 1), c + new Vector2(-4, -11), c + new Vector2(4, -11), c + new Vector2(4, 1)], color, w, true);
                DrawLine(c + new Vector2(-1.5f, -7), c + new Vector2(1.5f, -4), color, w, true);
                DrawLine(c + new Vector2(1.5f, -4), c + new Vector2(3, -8), color, w, true);
                break;
            case "laws":
                // Документ с загнутым углом и строками.
                DrawPolyline([c + new Vector2(-8, -11), c + new Vector2(4, -11), c + new Vector2(9, -6), c + new Vector2(9, 11),
                    c + new Vector2(-8, 11), c + new Vector2(-8, -11)], color, w, true);
                DrawPolyline([c + new Vector2(4, -11), c + new Vector2(4, -6), c + new Vector2(9, -6)], color, w, true);
                for (var i = 0; i < 3; i++)
                    DrawLine(c + new Vector2(-4, -2 + i * 5), c + new Vector2(5, -2 + i * 5), color, w, true);
                break;
            case "budget":
                // Круговая диаграмма с выделенным сектором.
                DrawArc(c + new Vector2(-1, 1), 10, 0, Mathf.Tau, 40, color, w, true);
                DrawLine(c + new Vector2(-1, 1), c + new Vector2(-1, -9), color, w, true);
                DrawLine(c + new Vector2(-1, 1), c + new Vector2(8, 5), color, w, true);
                DrawColoredPolygon(Sector(c + new Vector2(2, -2), 9, -Mathf.Pi / 2, 0.36f), color);
                break;
            case "society":
                // Три фигуры: люди.
                foreach (var (dx, r) in new[] { (-8f, 3.2f), (8f, 3.2f), (0f, 4f) })
                {
                    var head = c + new Vector2(dx, dx == 0 ? -5 : -3);
                    DrawArc(head, r, 0, Mathf.Tau, 24, color, w, true);
                    DrawArc(head + new Vector2(0, r + 8), r + 3.5f, Mathf.Pi, Mathf.Tau, 24, color, w, true);
                }
                break;
        }
    }

    static Vector2[] Sector(Vector2 center, float radius, float from, float to)
    {
        var points = new List<Vector2> { center };
        const int steps = 12;
        for (var i = 0; i <= steps; i++)
        {
            var a = from + (to - from) * i / steps;
            points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
        return points.ToArray();
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationMouseEnter or (int)NotificationMouseExit)
            QueueRedraw();
    }
}
