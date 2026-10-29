using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>Поражение на выборах: продолжить за новое правительство той же страны или закончить партию с итогами правления.</summary>
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
        Title.Text = "Вы проиграли выборы";
        Subtitle.Text = $"{countryName}, {Fmt.Date(defeat.Date)}";
        Content.AddChild(Line($"Победила {Fmt.Quote(defeat.NewParty)}. {defeat.OldLeader} уходит в оппозицию, страну возглавляет {defeat.NewLeader}.", 15,
            UiTheme.TextPrimary));
        Content.AddChild(Line("Можно продолжить игру за новое правительство: те же страна и экономика, но другие партия, министры и обещания. " +
                              "Или закончить партию и посмотреть итоги своего правления.", 14));
        Visible = true;
    }
}
