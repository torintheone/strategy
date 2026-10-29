using Godot;
using Politeconomy.Sim.Engine;

namespace Politeconomy.Game.UI;

/// <summary>
/// Парламент полукругом: каждое место — точка; оппозиция слева, правящий блок справа (золотом).
/// Если мест больше тысячи, точки рисуются мельче и реже — важны доли, а не каждое место.
/// </summary>
public partial class ParliamentChart : Control
{
    static readonly Color Ruling = UiTheme.Accent;
    static readonly Color Opposition = Color.FromHtml("#6f8fb8");
    ParliamentSnapshot? _parliament;

    public ParliamentChart()
    {
        CustomMinimumSize = new Vector2(240, 128);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(ParliamentSnapshot parliament)
    {
        if (_parliament == parliament)
            return;
        _parliament = parliament;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_parliament is not { Seats: > 0 } p)
            return;
        var size = Size;
        var center = new Vector2(size.X / 2, size.Y - 4);
        var outer = Math.Min(size.X / 2 - 4, size.Y - 8);
        var inner = outer * 0.38f;
        var shown = Math.Min(p.Seats, 720);
        var ruling = (int)Math.Round((double)p.Ruling / p.Seats * shown);
        // Ряды: в каждом ряду мест пропорционально радиусу; всего мест — shown.
        var rows = Math.Max(2, (int)Math.Round(Math.Sqrt(shown / 9.0)));
        var dr = (outer - inner) / rows;
        var lengths = Enumerable.Range(0, rows).Select(r => inner + (r + 0.5f) * dr).ToArray();
        var total = lengths.Sum();
        var counts = lengths.Select(l => (int)Math.Floor(shown * l / total)).ToArray();
        counts[^1] += shown - counts.Sum();
        var seats = new List<(float Angle, Vector2 Pos)>(shown);
        for (var r = 0; r < rows; r++)
        {
            var radius = lengths[r];
            for (var i = 0; i < counts[r]; i++)
            {
                var a = counts[r] == 1 ? Mathf.Pi / 2 : Mathf.Pi * i / (counts[r] - 1);
                seats.Add((a, center + new Vector2(-Mathf.Cos(a), -Mathf.Sin(a)) * radius));
            }
        }
        seats.Sort((x, y) => x.Angle.CompareTo(y.Angle));
        var dot = Math.Clamp(dr * 0.36f, 1.2f, 5f);
        for (var i = 0; i < seats.Count; i++)
            DrawCircle(seats[i].Pos, dot, i >= seats.Count - ruling ? Ruling : Opposition);
    }
}
