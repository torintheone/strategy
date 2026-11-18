using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Новая стройка: что даст, сколько стоит и сколько строить; как платить — из бюджета, в кредит у другой страны или в
/// концессию. У каждого способа написано, что будет.
/// </summary>
public partial class ProjectDialog : ModalDialog
{
    GameData _data = null!;

    /// <summary>Страна, стройка, способ (budget, loan, ppp), кредитор.</summary>
    public event Action<string, string, string, string?>? Chosen;

    public void Setup(GameData data)
    {
        _data = data;
        InitDialog(640);
    }

    public void Show(string countryId, string countryName, ProjectOfferSnapshot offer)
    {
        Title.Text = offer.Name;
        Subtitle.Text = $"{countryName} · {offer.Effect} · {Fmt.Number(offer.Cost, 2)}% ВВП · {Fmt.Plural(offer.Months, "месяц", "месяца", "месяцев")}";
        ClearContent();
        Content.AddChild(Line(offer.Description + " Цена и срок — с учётом коррупции: чем её больше, тем дороже и дольше.", 14, UiTheme.TextPrimary));
        var record = _data.Infrastructure.Projects.First(p => p.Id == offer.Id);
        var item = _data.Budget.Spending.First(s => s.Id == Politeconomy.Sim.Infrastructure.InfrastructureModel.Item(_data, record));
        var perYear = offer.Cost * 12 / Math.Max(1, offer.Months);
        Option("Из бюджета", $"Расходы «{item.Name}» вырастут на {Fmt.Number(perYear, 2)}% ВВП в год, пока идёт стройка. Дефицит и долг — ваши.",
            countryId, offer.Id, "budget", null, true);
        foreach (var lender in _data.Infrastructure.Lenders.Where(l => l != countryId).Take(3))
        {
            var name = _data.Countries.Countries.First(c => c.Id == lender).Name;
            Option($"Кредит: {name}", $"{name} оплатит стройку: бюджет сейчас не тратит ни копейки, но госдолг сразу вырастет на {Fmt.Number(offer.Cost, 2)}% ВВП, " +
                                      $"а у страны-кредитора появится рычаг давления на вас.", countryId, offer.Id, "loan", lender, true);
        }
        Option("Концессия", offer.PppAllowed
                ? $"Строит частный инвестор: бюджет не платит, но потом {_data.InfrastructureBalance.ConcessionYears:0} лет люди платят за пользование " +
                  "и недовольны этим, а инвестор берёт свою прибыль."
                : record.Kind == "repair" ? "Ремонт в концессию не берут: на нём нельзя заработать платой за проезд."
                : "Инвесторы не придут: коррупция слишком высокая.",
            countryId, offer.Id, "ppp", null, offer.PppAllowed);
        ClearButtons();
        AddButton("Отмена", false, () => Visible = false);
        Visible = true;
    }

    void Option(string title, string text, string countryId, string projectId, string financing, string? lender, bool enabled)
    {
        // Карточка, а не кнопка: её высота следует за текстом.
        var normal = UiTheme.Box(new Color(1, 1, 1, enabled ? 0.04f : 0.015f), UiTheme.Hairline, 10, 14, 8);
        var hover = UiTheme.Box(new Color(0.88f, 0.68f, 0.32f, 0.12f), UiTheme.Accent, 10, 14, 8);
        var card = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = enabled ? CursorShape.PointingHand : CursorShape.Arrow,
        };
        card.AddThemeStyleboxOverride("panel", normal);
        if (enabled)
        {
            card.MouseEntered += () => card.AddThemeStyleboxOverride("panel", hover);
            card.MouseExited += () => card.AddThemeStyleboxOverride("panel", normal);
        }
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 1);
        card.AddChild(column);
        var name = UiTheme.Text(title, 15, UiTheme.SemiBold, enabled ? UiTheme.TextPrimary : UiTheme.TextMuted);
        name.MouseFilter = MouseFilterEnum.Ignore;
        column.AddChild(name);
        var label = UiTheme.Text(text, 12, null, enabled ? UiTheme.TextSecondary : UiTheme.TextMuted);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.MouseFilter = MouseFilterEnum.Ignore;
        column.AddChild(label);
        if (enabled)
            card.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    return;
                Visible = false;
                Chosen?.Invoke(countryId, projectId, financing, lender);
            };
        Content.AddChild(card);
    }
}
