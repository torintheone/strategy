using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Переговоры о коалиции: у вашей партии нет большинства. Для каждой партии — места, сколько постов она попросит
/// и какой закон потребует принять (иначе через два года выйдет из коалиции); далёкие по взглядам отказываются.
/// Отмечаете партнёров — внизу видно, будет ли большинство.
/// </summary>
public partial class CoalitionDialog : ModalDialog
{
    GameData _data = null!;
    readonly List<(CheckBox Box, OfferSnapshot Offer)> _rows = new();
    Label _total = null!;
    int _headSeats;
    int _seats;
    string? _country;

    public event Action<string, IReadOnlyList<string>>? Formed;

    public void Setup(GameData data)
    {
        _data = data;
        InitDialog(680);
        AddButton("Решить позже", false, () => Visible = false);
        AddButton("Собрать коалицию", true, () =>
        {
            if (_country == null)
                return;
            Formed?.Invoke(_country, _rows.Where(r => r.Box.ButtonPressed).Select(r => r.Offer.Party).ToList());
            Visible = false;
        });
    }

    public void Show(string countryId, string countryName, CountrySnapshot country)
    {
        if (country.Politics is not { Talks: { } talks } p || country.Government?.Parliament is not { } parliament)
            return;
        _country = countryId;
        _seats = parliament.Seats;
        var head = p.Head;
        _headSeats = head?.Seats ?? 0;
        ClearContent();
        _rows.Clear();
        Title.Text = $"Переговоры о коалиции: {countryName}";
        Subtitle.Text = $"У партии {Fmt.Quote(head?.Name ?? "")} {Fmt.Number(_headSeats, 0)} мест из {Fmt.Number(_seats, 0)} — для большинства нужно " +
                        $"{Fmt.Number(_seats / 2 + 1, 0)}. Решите до {Fmt.Date(talks.Deadline)}, иначе коалицию соберут без вас.";
        foreach (var offer in talks.Offers.OrderBy(o => o.Refuses).ThenByDescending(o => o.Seats))
        {
            var party = p.Party(offer.Party);
            if (party == null)
                continue;
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.04f), UiTheme.Hairline, 10, 12, 8));
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            card.AddChild(row);
            var box = new CheckBox { FocusMode = FocusModeEnum.None, Disabled = offer.Refuses, SizeFlagsVertical = SizeFlags.ShrinkBegin };
            box.Toggled += _ => UpdateTotal();
            row.AddChild(box);
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", 2);
            row.AddChild(column);
            var top = new HBoxContainer();
            top.AddThemeConstantOverride("separation", 8);
            top.AddChild(Dot(party.Color));
            var name = UiTheme.Text(party.Name, 15, UiTheme.SemiBold, offer.Refuses ? UiTheme.TextMuted : UiTheme.TextPrimary);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            top.AddChild(name);
            top.AddChild(UiTheme.Text($"{Fmt.Number(offer.Seats, 0)} мест", 14, UiTheme.Numbers, UiTheme.TextPrimary));
            column.AddChild(top);
            column.AddChild(Line(Terms(offer), 13, offer.Refuses ? UiTheme.Critical : UiTheme.TextSecondary));
            Content.AddChild(card);
            _rows.Add((box, offer));
        }
        _total = Line("", 14, UiTheme.TextPrimary, UiTheme.Medium);
        Content.AddChild(_total);
        UpdateTotal();
        Visible = true;
    }

    string Terms(OfferSnapshot offer)
    {
        if (offer.Refuses)
            return "Отказывается: слишком далеки по взглядам.";
        var posts = Math.Max(1, (int)Math.Round(18.0 * offer.Seats / Math.Max(1, _headSeats + offer.Seats)));
        var text = $"Попросит около {Fmt.Plural(posts, "поста", "постов", "постов")} в правительстве.";
        if (offer.Law != null && offer.Variant != null)
        {
            var law = _data.Law(offer.Law);
            text += $" Потребует принять закон «{law.Name}: {law.Variant(offer.Variant).Name.ToLowerInvariant()}» в течение двух лет — иначе выйдет из коалиции.";
        }
        else
            text += " Особых условий нет.";
        return text;
    }

    void UpdateTotal()
    {
        var seats = _headSeats + _rows.Where(r => r.Box.ButtonPressed).Sum(r => r.Offer.Seats);
        _total.Text = seats * 2 > _seats
            ? $"У коалиции будет {Fmt.Number(seats, 0)} мест из {Fmt.Number(_seats, 0)} — большинство."
            : $"У коалиции будет {Fmt.Number(seats, 0)} мест из {Fmt.Number(_seats, 0)} — правительство меньшинства: законы пройдут, только если их поддержит оппозиция.";
        _total.AddThemeColorOverride("font_color", seats * 2 > _seats ? Color.FromHtml("#7fc49a") : UiTheme.Accent);
    }
}
