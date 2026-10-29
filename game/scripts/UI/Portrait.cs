using Godot;

namespace Politeconomy.Game.UI;

/// <summary>
/// Заглушка портрета, пока нет иллюстраций (этап 8): силуэт по пояс на мягком фоне. Женский силуэт — с длинными
/// волосами, у людей старше 60 — седина. Цвет фона слегка разный у разных людей, чтобы карточки не сливались.
/// </summary>
public partial class Portrait : Control
{
    bool _female;
    int _age = 50;
    int _seed;

    public Portrait()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    public void Show(bool female, int age, int seed)
    {
        (_female, _age, _seed) = (female, age, seed);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var s = Size;
        var hue = 0.55f + (_seed * 0.6180339f % 1) * 0.12f;
        var top = Color.FromHsv(hue, 0.28f, 0.30f);
        var bottom = Color.FromHsv(hue, 0.32f, 0.17f);
        DrawPolygon([Vector2.Zero, new Vector2(s.X, 0), s, new Vector2(0, s.Y)], [top, top, bottom, bottom]);

        var body = new Color(0.07f, 0.085f, 0.11f);
        var hair = _age >= 60 ? new Color(0.62f, 0.64f, 0.67f) : new Color(0.05f, 0.06f, 0.08f);
        var cx = s.X / 2;
        var headR = s.X * 0.17f;
        var headY = s.Y * 0.40f;
        if (_female)
        {
            // Волосы ниже плеч позади головы.
            DrawColoredPolygon(Ellipse(new Vector2(cx, headY + headR * 0.5f), headR * 1.35f, headR * 1.9f), hair);
        }
        // Плечи и торс.
        DrawColoredPolygon(Ellipse(new Vector2(cx, s.Y * 1.02f), s.X * 0.42f, s.Y * 0.36f), body);
        DrawRect(new Rect2(cx - headR * 0.45f, headY + headR * 0.6f, headR * 0.9f, headR * 1.2f), body);
        // Голова.
        DrawCircle(new Vector2(cx, headY), headR, body);
        if (!_female && _age >= 60)
            DrawArc(new Vector2(cx, headY), headR, Mathf.Pi * 1.05f, Mathf.Pi * 1.95f, 24, hair, headR * 0.28f, true);
        else if (!_female)
            DrawArc(new Vector2(cx, headY), headR, Mathf.Pi * 1.05f, Mathf.Pi * 1.95f, 24, hair.Lightened(0.05f), headR * 0.22f, true);
        // Тонкая рамка.
        DrawRect(new Rect2(Vector2.Zero, s), new Color(1, 1, 1, 0.08f), false, 1);
    }

    static Vector2[] Ellipse(Vector2 center, float rx, float ry)
    {
        var points = new Vector2[32];
        for (var i = 0; i < points.Length; i++)
        {
            var a = Mathf.Tau * i / points.Length;
            points[i] = center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return points;
    }
}
