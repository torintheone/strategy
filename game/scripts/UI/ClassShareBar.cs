using Godot;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>Полоса долей классов в населении; доли плавно перетекают при смене страны и со временем.</summary>
public partial class ClassShareBar : Control
{
    Color[] _colors = [];
    double[] _target = [];
    double[] _shown = [];
    string[] _names = [];

    public ClassShareBar()
    {
        CustomMinimumSize = new Vector2(0, 14);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void Setup(Color[] colors)
    {
        _colors = colors;
        _target = new double[colors.Length];
        _shown = new double[colors.Length];
    }

    public void Show(double[] shares, string[] names)
    {
        _target = shares;
        _names = names;
        QueueRedraw();
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        var total = _shown.Sum();
        if (total <= 0 || Size.X <= 0)
            return "";
        var x = 0.0;
        for (var k = 0; k < _shown.Length; k++)
        {
            x += _shown[k] / total * Size.X;
            if (atPosition.X <= x && k < _names.Length)
                return $"{_names[k]}: {Fmt.Value("percent", _target[k])} населения";
        }
        return "";
    }

    public override void _Process(double delta)
    {
        var k = 1 - Mathf.Exp(-10 * (float)delta);
        var changed = false;
        for (var i = 0; i < _shown.Length && i < _target.Length; i++)
        {
            if (Math.Abs(_shown[i] - _target[i]) < 1e-3)
                continue;
            _shown[i] += (_target[i] - _shown[i]) * k;
            changed = true;
        }
        if (changed)
            QueueRedraw();
    }

    public override void _Draw()
    {
        var total = _shown.Sum();
        if (total <= 0)
            return;
        var height = Size.Y - 4;
        var x = 0f;
        for (var k = 0; k < _shown.Length; k++)
        {
            var w = (float)(_shown[k] / total) * Size.X;
            if (w > 0.5f)
                DrawRect(new Rect2(x, 0, Math.Max(0, w - 1), height), _colors[k]);
            x += w;
        }
    }
}
