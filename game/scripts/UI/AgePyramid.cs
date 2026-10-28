using Godot;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>Возрастная пирамида: мужчины слева, женщины справа, пятилетние группы снизу вверх (0–4 … 80+).</summary>
public partial class AgePyramid : Control
{
    const int Groups = 17;
    const float LabelWidth = 34;
    const float RowGap = 2;

    static readonly Color MaleColor = Color.FromHtml("#5a7fb3");
    static readonly Color FemaleColor = Color.FromHtml("#b5607f");

    IReadOnlyList<double>? _ages;
    double[] _shown = new double[Groups * 2];

    public AgePyramid()
    {
        CustomMinimumSize = new Vector2(0, 176);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void Show(IReadOnlyList<double>? ages)
    {
        _ages = ages;
        if (ages == null)
            return;
        var total = ages.Sum();
        TooltipText = $"Возрастная пирамида: мужчины слева, женщины справа.\nДо 15 лет: {Share(0, 2)}, 15–64: {Share(3, 12)}, 65 и старше: {Share(13, 16)}.";
        string Share(int from, int to)
        {
            var sum = 0.0;
            for (var g = from; g <= to; g++)
                sum += ages[g] + ages[Groups + g];
            return Fmt.Value("percent", sum / total * 100);
        }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Полоски плавно доезжают до новых значений, когда меняется страна или проходит время.
        if (_ages == null)
            return;
        var k = 1 - Mathf.Exp(-10 * (float)delta);
        var changed = false;
        for (var i = 0; i < _shown.Length; i++)
        {
            var target = _ages[i];
            if (Math.Abs(_shown[i] - target) <= Math.Max(1, target * 1e-4))
                continue;
            _shown[i] += (target - _shown[i]) * k;
            changed = true;
        }
        if (changed)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (_ages == null)
            return;
        var size = Size;
        var max = _shown.Max();
        if (max <= 0)
            return;
        var half = (size.X - LabelWidth) / 2;
        var rowHeight = (size.Y - 14) / Groups;
        var font = UiTheme.Regular;
        for (var g = 0; g < Groups; g++)
        {
            var y = (Groups - 1 - g) * rowHeight;
            var male = (float)(_shown[g] / max) * (half - 4);
            var female = (float)(_shown[Groups + g] / max) * (half - 4);
            DrawRect(new Rect2(half - male, y + RowGap / 2, male, rowHeight - RowGap), MaleColor);
            DrawRect(new Rect2(half + LabelWidth, y + RowGap / 2, female, rowHeight - RowGap), FemaleColor);
            if (g % 4 == 0 || g == Groups - 1)
            {
                var label = g == Groups - 1 ? "80+" : (g * 5).ToString();
                var w = font.GetStringSize(label, HorizontalAlignment.Left, -1, 11).X;
                DrawString(font, new Vector2(half + (LabelWidth - w) / 2, y + rowHeight - 2), label, HorizontalAlignment.Left, -1, 11, UiTheme.TextMuted);
            }
        }
        DrawString(font, new Vector2(0, size.Y - 1), "мужчины", HorizontalAlignment.Left, -1, 11, MaleColor.Lightened(0.25f));
        var fw = font.GetStringSize("женщины", HorizontalAlignment.Left, -1, 11).X;
        DrawString(font, new Vector2(size.X - fw, size.Y - 1), "женщины", HorizontalAlignment.Left, -1, 11, FemaleColor.Lightened(0.25f));
    }
}
