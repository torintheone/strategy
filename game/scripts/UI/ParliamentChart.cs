using Godot;
using Politeconomy.Sim.Engine;

namespace Politeconomy.Game.UI;

/// <summary>
/// Парламент полукругом: каждое место — точка. Если есть партии, места окрашены в их цвета и идут слева направо
/// от левых партий к правым (по экономической оси); без партий — оппозиция слева, правящий блок справа (золотом).
/// Если мест больше тысячи, точки рисуются мельче и реже — важны доли, а не каждое место.
/// </summary>
public partial class ParliamentChart : Control
{
    static readonly Color Ruling = UiTheme.Accent;
    static readonly Color Opposition = Color.FromHtml("#6f8fb8");
    ParliamentSnapshot? _parliament;
    PoliticsSnapshot? _politics;

    public ParliamentChart()
    {
        CustomMinimumSize = new Vector2(240, 128);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(ParliamentSnapshot parliament, PoliticsSnapshot? politics = null)
    {
        if (_parliament == parliament && _politics == politics)
            return;
        (_parliament, _politics) = (parliament, politics);
        QueueRedraw();
    }

    /// <summary>Отрезки полукруга слева направо: сколько мест и каким цветом.</summary>
    List<(int Seats, Color Color)> Segments(ParliamentSnapshot p)
    {
        if (_politics != null && p.Factions.Count > 0 && p.Factions.All(f => _politics.Party(f.Party) != null))
            return p.Factions
                .Select(f => _politics.Party(f.Party)!)
                .OrderBy(x => x.Position[0])
                .Select(x => (p.Factions.First(f => f.Party == x.Id).Seats, UiTheme.Hex(x.Color)))
                .ToList();
        return [(p.Opposition, Opposition), (p.Ruling, Ruling)];
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
        // Доли отрезков переводятся в показанные места; последний отрезок добирает остаток от округлений.
        var segments = Segments(p);
        var colors = new List<Color>(seats.Count);
        foreach (var (count, color) in segments)
            colors.AddRange(Enumerable.Repeat(color, (int)Math.Round((double)count / p.Seats * shown)));
        while (colors.Count < seats.Count)
            colors.Add(segments[^1].Color);
        for (var i = 0; i < seats.Count; i++)
            DrawCircle(seats[i].Pos, dot, colors[i]);
    }
}
