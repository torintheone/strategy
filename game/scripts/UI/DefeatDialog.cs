using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Потеря власти — на выборах, вотумом недоверия, импичментом, переворотом или революцией: продолжить за новое
/// правительство той же страны или закончить партию с итогами правления.
/// </summary>
public partial class DefeatDialog : ModalDialog
{
    public event Action? ContinueChosen;
    public event Action? EndChosen;

    public void Setup()
    {
        InitDialog(560);
        AddButton("Закончить партию", false, () =>
        {
            Visible = false;
            EndChosen?.Invoke();
        });
        AddButton("Продолжить за новое правительство", true, () =>
        {
            Visible = false;
            ContinueChosen?.Invoke();
        });
    }

    public void Show(Defeat defeat, string countryName)
    {
        ClearContent();
        var party = string.IsNullOrEmpty(defeat.NewParty) ? "" : $" ({Fmt.Quote(defeat.NewParty)})";
        (Title.Text, var text) = defeat.Reason switch
        {
            "no_confidence" => ("Правительство отправлено в отставку",
                $"Парламент выразил недоверие правительству. {defeat.OldLeader} уходит, новое правительство возглавил {defeat.NewLeader}{party}."),
            "impeachment" => ("Импичмент", $"Парламент отстранил {defeat.OldLeader} от власти. Страну возглавил {defeat.NewLeader}{party}."),
            "palace_coup" => ("Дворцовый переворот", $"Элиты сговорились за спиной {defeat.OldLeader}. Власть взял {defeat.NewLeader}{party}."),
            "military_coup" => ("Военный переворот",
                $"Армия свергла {defeat.OldLeader}. Страной правит военный совет во главе с {defeat.NewLeader}: парламент распущен, выборов нет."),
            "revolution" => ("Революция", $"Восставшие свергли {defeat.OldLeader}. Временное правительство возглавил {defeat.NewLeader}{party}; скоро выборы."),
            _ => ("Вы проиграли выборы", $"Победила {Fmt.Quote(defeat.NewParty)}. {defeat.OldLeader} уходит в оппозицию, страну возглавляет {defeat.NewLeader}."),
        };
        Subtitle.Text = $"{countryName}, {Fmt.Date(defeat.Date)}";
        Content.AddChild(Line(text, 15, UiTheme.TextPrimary));
        Content.AddChild(Line("Можно продолжить игру за новое правительство: те же страна и экономика, но другие глава, министры и обещания. " +
                              "Или закончить партию и посмотреть итоги своего правления.", 14));
        Visible = true;
    }
}
