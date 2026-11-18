using Godot;

namespace Politeconomy.Game.UI;

/// <summary>Кнопка паузы: рисует значок сама, потому что в шрифте нет символов ▶ и ⏸.</summary>
public partial class PlayButton : Button
{
    bool _paused = true;

    public PlayButton()
    {
        CustomMinimumSize = new Vector2(34, 30);
        FocusMode = FocusModeEnum.None;
    }

    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value)
                return;
            _paused = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var c = Size / 2;
        var color = _paused ? UiTheme.Accent : UiTheme.TextPrimary;
        if (_paused)
        {
            DrawColoredPolygon([c + new Vector2(-4, -7), c + new Vector2(7, 0), c + new Vector2(-4, 7)], color);
        }
        else
        {
            DrawRect(new Rect2(c.X - 6, c.Y - 7, 4, 14), color);
            DrawRect(new Rect2(c.X + 2, c.Y - 7, 4, 14), color);
        }
    }
}
