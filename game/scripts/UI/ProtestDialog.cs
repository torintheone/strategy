using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Протест в вашей стране: кто вышел, стадия, повод и требование, сколько людей на улицах. Четыре ответа — уступить,
/// договориться, промолчать, разогнать — у каждого написано, что будет и с каким шансом.
/// </summary>
public partial class ProtestDialog : ModalDialog
{
    GameData _data = null!;

    public event Action<string, int, string>? Answered;

    public void Setup(GameData data)
    {
        _data = data;
        InitDialog(640);
    }

    public void Show(string countryId, string countryName, ProtestSnapshot protest, SecuritySnapshot? security)
    {
        var record = _data.Classes.Classes[protest.Class];
        Title.Text = $"{record.Name}: {protest.StageName}";
        Subtitle.Text = $"{countryName} · {Capital(protest.CauseText)} · на улицах около {Fmt.People(protest.Participants)} человек" +
                        (protest.Months > 0 ? $" · {Fmt.Plural(protest.Months, "месяц", "месяца", "месяцев")}" : "");
        ClearContent();
        Content.AddChild(Line(Stakes(protest), 14, UiTheme.TextPrimary));
        var law = protest.Law != null && protest.Variant != null ? _data.Law(protest.Law) : null;
        var concede = law != null
            ? $"Вернуть закон «{law.Name}» к варианту «{law.Variant(protest.Variant!).Name.ToLowerInvariant()}». Протест закончится, но закон пропадёт."
            : "Дать деньги: расходы бюджета на нужды класса вырастут, настроение класса поднимется, протест стихнет на две стадии.";
        var force = protest.Army ? "армию" : "полицию";
        var loyalty = protest.Army ? security?.ArmyLoyalty : security?.PoliceLoyalty;
        Option("Уступить", concede, "concede", countryId, protest);
        Option("Переговоры", $"Шанс договориться — {Percent(protest.NegotiateChance)} (чем организованнее класс, тем легче). Если удастся, протест стихнет.",
            "negotiate", countryId, protest);
        Option("Игнорировать", "Ничего не делать. Протест может утихнуть сам — или разгореться.", "ignore", countryId, protest);
        Option("Разогнать", $"Послать {force}: шанс — {Percent(protest.DisperseChance)}" +
                            (loyalty is { } l ? $", лояльность {Fmt.Number(l, 0)}" : "") +
                            ". Даже удачный разгон злит людей (радикализм растёт), бьёт по свободам и легитимности и изматывает силовиков; " +
                            "неудачный — разжигает протест.", "disperse", countryId, protest);
        ClearButtons();
        AddButton("Решить позже", false, () => Visible = false);
        Visible = true;
    }

    static string Stakes(ProtestSnapshot protest) => protest.Stage switch
    {
        0 => "Работа встала: страдает экономика. Пока это не угроза власти.",
        1 => "Митинги собирают сторонников. Если ничего не делать, протест может разрастись.",
        2 => "Массовые протесты: падает легитимность, инвесторы нервничают, рейтинг правящих тает.",
        3 => "Беспорядки: экономика теряет, легитимность рушится, против протестующих придётся бросить армию.",
        _ => "Восстание: власть под угрозой. Если армия не встанет на её сторону, режим может пасть.",
    };

    void Option(string title, string text, string response, string countryId, ProtestSnapshot protest)
    {
        var button = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 58) };
        button.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(1, 1, 1, 0.04f), UiTheme.Hairline, 10, 14, 8));
        button.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.88f, 0.68f, 0.32f, 0.12f), UiTheme.Accent, 10, 14, 8));
        button.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(0.88f, 0.68f, 0.32f, 0.2f), UiTheme.Accent, 10, 14, 8));
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        (column.OffsetLeft, column.OffsetRight, column.OffsetTop, column.OffsetBottom) = (14, -14, 7, -7);
        column.AddThemeConstantOverride("separation", 1);
        button.AddChild(column);
        var name = UiTheme.Text(title, 15, UiTheme.SemiBold, response == "disperse" ? UiTheme.Critical : UiTheme.TextPrimary);
        name.MouseFilter = MouseFilterEnum.Ignore;
        column.AddChild(name);
        var label = UiTheme.Text(text, 12, null, UiTheme.TextSecondary);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.MouseFilter = MouseFilterEnum.Ignore;
        column.AddChild(label);
        // Высота кнопки — по тексту: метки внутри кнопки её размер не задают.
        button.Resized += () => button.CustomMinimumSize = new Vector2(0, Math.Max(58, column.GetCombinedMinimumSize().Y + 14));
        button.Pressed += () =>
        {
            Visible = false;
            Answered?.Invoke(countryId, protest.Id, response);
        };
        Content.AddChild(button);
    }

    static string Percent(double chance) => $"{Fmt.Number(chance * 100, 0)}%";

    static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
