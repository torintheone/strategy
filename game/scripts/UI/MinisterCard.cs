using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;

namespace Politeconomy.Game.UI;

/// <summary>Карточка министра: силуэт, пост, имя, возраст и черты, полоски качеств, кнопка замены (в своей стране).</summary>
public partial class MinisterCard : PanelContainer
{
    Portrait _portrait = null!;
    Label _post = null!;
    Label _name = null!;
    Label _info = null!;
    readonly Meter[] _meters = [new(), new(), new()];
    Button _replace = null!;

    public event Action? ReplaceRequested;

    public void Setup(PostRecord post)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(300, 0);
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.035f), UiTheme.Hairline, 12, 12, 10));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        AddChild(row);
        _portrait = new Portrait { CustomMinimumSize = new Vector2(60, 78), SizeFlagsVertical = SizeFlags.ShrinkBegin };
        row.AddChild(_portrait);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 2);
        row.AddChild(column);
        var top = new HBoxContainer();
        column.AddChild(top);
        _post = UiTheme.Text(post.Short.ToUpperInvariant(), 11, UiTheme.MapCountryFont, UiTheme.TextMuted);
        _post.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _post.TooltipText = post.Name;
        top.AddChild(_post);
        _replace = new Button { Text = "Сменить", Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Предложить замену на этот пост" };
        _replace.AddThemeFontSizeOverride("font_size", 12);
        _replace.AddThemeColorOverride("font_color", UiTheme.Accent);
        _replace.Pressed += () => ReplaceRequested?.Invoke();
        top.AddChild(_replace);
        _name = UiTheme.Text("", 15, UiTheme.SemiBold);
        _name.ClipText = true;
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(_name);
        _info = UiTheme.Text("", 12, null, UiTheme.TextSecondary);
        _info.ClipText = true;
        _info.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(_info);
        foreach (var m in _meters)
            column.AddChild(m);
    }

    /// <summary>Info — возраст и черты одной строкой.</summary>
    public void Show(CharacterSnapshot person, string info, string tooltip, bool canReplace)
    {
        _portrait.Show(person.Female, person.Age, person.Id);
        _name.Text = person.Name;
        _info.Text = info;
        var qualities = GovernmentScreen.Qualities(person);
        for (var i = 0; i < _meters.Length; i++)
            _meters[i].Show(qualities[i].Label, qualities[i].Value, qualities[i].Color);
        TooltipText = tooltip;
        _replace.Visible = canReplace;
    }
}
