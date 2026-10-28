using Godot;

namespace Politeconomy.Game.UI;

/// <summary>Карточка у курсора: страна под мышью и её значение в текущем режиме карты.</summary>
public partial class HoverCard : GlassPanel
{
    Label _title = null!;
    Label _line = null!;

    public void Setup()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        InitGlass(radius: 10, padH: 12, padV: 8, tint: 0.86f);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 1);
        Body.AddChild(column);
        _title = UiTheme.Text("", 15, UiTheme.SemiBold);
        _line = UiTheme.Text("", 13, null, UiTheme.TextSecondary);
        column.AddChild(_title);
        column.AddChild(_line);
    }

    public void ShowAt(Vector2 mouse, string title, string line)
    {
        _title.Text = title;
        _line.Text = line;
        _line.Visible = line.Length > 0;
        var appearing = !Visible;
        Visible = true;
        ResetSize();
        var view = GetViewportRect().Size;
        var pos = mouse + new Vector2(18, 18);
        if (pos.X + Size.X > view.X - 8)
            pos.X = mouse.X - Size.X - 12;
        if (pos.Y + Size.Y > view.Y - 8)
            pos.Y = mouse.Y - Size.Y - 12;
        Position = pos;
        if (appearing)
            AnimateIn(Vector2.Zero, 0.12f, 0.96f);
    }
}
