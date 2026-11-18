using Godot;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Дебаты лидеров за месяц до выборов: ведущий задаёт вопрос по теме, о которой страна спорит сильнее всего, и вы
/// выбираете один из трёх ответов. У каждого — оценка штаба: классы, согласные с ответом, тянутся к партии, несогласные
/// отворачиваются. Обаяние лидера и удача выступления добавляют своё.
/// </summary>
public partial class DebateDialog : ModalDialog
{
    static readonly Color Good = Color.FromHtml("#7fc49a");
    string? _country;

    public event Action<string, int>? Answered;

    public void Setup() => InitDialog(640);

    public void Show(string countryId, string countryName, CharacterSnapshot? leader, DebateSnapshot debate)
    {
        _country = countryId;
        Title.Text = $"Дебаты лидеров: {countryName}";
        Subtitle.Text = $"Тема — {debate.Topic.ToLowerInvariant()}. Вопрос ведущего:";
        ClearContent();
        Content.AddChild(Line(debate.Question, 17, UiTheme.TextPrimary, UiTheme.SerifBold));
        for (var i = 0; i < debate.Answers.Count; i++)
        {
            var answer = i - 1;
            var gain = i < debate.Gain.Count ? debate.Gain[i] : 0;
            var card = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 52), Flat = false };
            card.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(1, 1, 1, 0.04f), UiTheme.Hairline, 10, 14, 8));
            card.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.88f, 0.68f, 0.32f, 0.12f), UiTheme.Accent, 10, 14, 8));
            card.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(0.88f, 0.68f, 0.32f, 0.2f), UiTheme.Accent, 10, 14, 8));
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            row.OffsetLeft = 14;
            row.OffsetRight = -14;
            row.AddThemeConstantOverride("separation", 12);
            card.AddChild(row);
            var text = UiTheme.Text($"«{debate.Answers[i]}»", 14, null, UiTheme.TextPrimary);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(text);
            var estimate = UiTheme.Text(Math.Abs(gain) < 0.0005 ? "≈0 п. п." : $"≈{Fmt.Signed(gain * 100, 1)} п. п.", 13, UiTheme.Numbers,
                gain > 0.0005 ? Good : gain < -0.0005 ? UiTheme.Critical : UiTheme.TextMuted);
            estimate.VerticalAlignment = VerticalAlignment.Center;
            estimate.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(estimate);
            card.Pressed += () =>
            {
                Visible = false;
                if (_country != null)
                    Answered?.Invoke(_country, answer);
            };
            Content.AddChild(card);
        }
        var charm = leader == null ? "" : $" У вас популярность {Fmt.Number(leader.Popularity, 0)}: " +
                                          (leader.Popularity >= 55 ? "зрители вас любят, это поможет." : leader.Popularity <= 40 ? "зрители настроены к вам прохладно." : "зрители настроены ровно.");
        Content.AddChild(Line("Оценки штаба — без учёта того, как пройдёт выступление: обаяние и удача прибавят или отнимут ещё немного." + charm,
            12, UiTheme.TextMuted));
        ClearButtons();
        AddButton("Подумать позже", false, () => Visible = false);
        Visible = true;
    }
}
