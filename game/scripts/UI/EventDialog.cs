using Godot;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Событие в вашей стране: министр-докладчик с портретом, что случилось и почему именно сейчас, варианты решения —
/// у каждого написано, что будет. Закрытый вариант объясняет, чего для него не хватает. Если не решить в срок,
/// сработает вариант по умолчанию.
/// </summary>
public partial class EventDialog : ModalDialog
{
    /// <summary>Номер события и выбранный вариант.</summary>
    public event Action<int, string>? Decided;

    public void Setup() => InitDialog(760);

    public void Show(PendingEventSnapshot e)
    {
        Title.Text = e.Title;
        Subtitle.Text = $"{e.Category} · {Fmt.Date(e.Date)} · решить до {Fmt.Date(e.Deadline)}";
        Subtitle.Visible = true;
        ClearContent();

        // Цветная полоса категории — место будущей иллюстрации (этап 8).
        var stripe = new ColorRect { Color = UiTheme.Hex(e.Color), CustomMinimumSize = new Vector2(0, 3), MouseFilter = MouseFilterEnum.Ignore };
        Content.AddChild(stripe);

        var report = new HBoxContainer();
        report.AddThemeConstantOverride("separation", 14);
        Content.AddChild(report);
        var portrait = new Portrait { CustomMinimumSize = new Vector2(72, 94), SizeFlagsVertical = SizeFlags.ShrinkBegin };
        if (e.Reporter is { } who)
            portrait.Show(who.Female, who.Age, who.Id);
        report.AddChild(portrait);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 4);
        report.AddChild(column);
        if (e.Reporter is { } reporter)
            column.AddChild(UiTheme.Text($"Докладывает: {e.ReporterPost.ToLowerInvariant()} {reporter.Name}", 12, UiTheme.Medium, UiTheme.Accent));
        column.AddChild(Line(e.Text, 15, UiTheme.TextPrimary));
        if (e.Why.Count > 0)
            column.AddChild(Line("Почему сейчас: " + string.Join("; ", e.Why) + ".", 12, UiTheme.TextMuted));

        foreach (var option in e.Options)
            Content.AddChild(Card(e, option));
        ClearButtons();
        AddButton("Решить позже", false, () => Visible = false);
        Visible = true;
    }

    Control Card(PendingEventSnapshot e, EventOptionSnapshot option)
    {
        // Карточка, а не кнопка: её высота следует за текстом.
        var normal = UiTheme.Box(new Color(1, 1, 1, 0.04f), UiTheme.Hairline, 10, 14, 8);
        var hover = UiTheme.Box(new Color(0.88f, 0.68f, 0.32f, 0.12f), UiTheme.Accent, 10, 14, 8);
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", normal);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 1);
        card.AddChild(column);
        var name = UiTheme.Text(option.Name + (option.Default ? "  ·  сработает само, если не решить в срок" : ""), 15, UiTheme.SemiBold,
            option.Enabled ? UiTheme.TextPrimary : UiTheme.TextMuted);
        name.MouseFilter = MouseFilterEnum.Ignore;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(name);
        column.AddChild(Passive(Line(option.Text, 13, UiTheme.TextSecondary)));
        if (!option.Enabled)
        {
            column.AddChild(Passive(Line(option.Reason ?? "Сейчас этот вариант недоступен.", 12, UiTheme.Critical)));
            return card;
        }
        column.AddChild(Passive(Line(string.Join(". ", option.Consequences) + ".", 12, UiTheme.TextMuted)));
        card.MouseDefaultCursorShape = CursorShape.PointingHand;
        card.MouseEntered += () => card.AddThemeStyleboxOverride("panel", hover);
        card.MouseExited += () => card.AddThemeStyleboxOverride("panel", normal);
        card.GuiInput += input =>
        {
            if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                return;
            Visible = false;
            Decided?.Invoke(e.Id, option.Id);
        };
        return card;
    }

    static Label Passive(Label label)
    {
        label.MouseFilter = MouseFilterEnum.Ignore;
        return label;
    }
}
