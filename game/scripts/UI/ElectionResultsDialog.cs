using Godot;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>
/// Итоги выборов: партии (или кандидаты) с долями голосов и изменением с прошлых выборов, места в парламенте
/// полукругом, явка, сменилась ли власть. Кнопка «Карта выборов» показывает победителей по регионам.
/// </summary>
public partial class ElectionResultsDialog : ModalDialog
{
    public event Action? MapRequested;

    public void Setup()
    {
        InitDialog(640);
        AddButton("Карта выборов", false, () =>
        {
            Visible = false;
            MapRequested?.Invoke();
        });
        AddButton("Закрыть", true, () => Visible = false);
    }

    public void Show(string countryName, CountrySnapshot country, ElectionSnapshot r)
    {
        var p = country.Politics!;
        ClearContent();
        var previous = p.Elections?.Where(e => e.Kind == r.Kind && e.Date < r.Date).LastOrDefault();
        var parliament = country.Government?.Parliament;
        Title.Text = r.Kind == "president" ? $"Президентские выборы: {countryName}" : $"Выборы: {parliament?.Name ?? "парламент"}, {countryName}";
        Subtitle.Text = $"{Fmt.Date(r.Date)} · явка {Fmt.Value("percent", r.Turnout * 100)}" + (r.Runoff != null ? " · понадобился второй тур" : "");

        if (r.Kind == "president")
            President(p, r);
        else
            Parliament(p, r, previous, parliament);

        var winner = p.Party(r.Winner);
        var outcome = r.Kind == "president"
            ? $"Избран{(Female(country, r) ? "а" : "")} {r.Candidates.GetValueOrDefault(r.Winner ?? "", "")} ({winner?.Short})."
            : $"Больше всех мест — у партии {Fmt.Quote(winner?.Name ?? "")}.";
        outcome += r.PowerChanged ? " Власть сменилась." : " Власть осталась у прежней партии.";
        Content.AddChild(Line(outcome, 15, r.PowerChanged ? UiTheme.Accent : UiTheme.TextPrimary, UiTheme.Medium));
        Visible = true;
    }

    static bool Female(CountrySnapshot country, ElectionSnapshot r) =>
        country.Government?.Leader is { } leader && leader.Party == r.Winner && leader.Female;

    void Parliament(PoliticsSnapshot p, ElectionSnapshot r, ElectionSnapshot? previous, ParliamentSnapshot? parliament)
    {
        if (parliament != null && r.Seats.Count > 0)
        {
            var chart = new ParliamentChart { CustomMinimumSize = new Vector2(360, 150), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
            var factions = r.Seats.Select(kv => new FactionSnapshot(kv.Key, kv.Value, p.Party(kv.Key)?.Ruling == true)).ToList();
            chart.Show(parliament with { Factions = factions, Seats = r.Seats.Values.Sum() }, p);
            Content.AddChild(chart);
        }
        Content.AddChild(Header("Партия", ("голоса", 70), ("±", 56), ("места", 56), ("±", 50)));
        foreach (var party in p.Parties.OrderByDescending(x => r.Votes.GetValueOrDefault(x.Id)))
        {
            var votes = r.Votes.GetValueOrDefault(party.Id) * 100;
            var seats = r.Seats.GetValueOrDefault(party.Id);
            var row = Row(party);
            row.AddChild(Cell(Fmt.Value("percent", votes), 70));
            row.AddChild(Cell(previous == null ? "" : Fmt.Signed(votes - previous.Votes.GetValueOrDefault(party.Id) * 100, 1), 56, UiTheme.TextMuted));
            row.AddChild(Cell(Fmt.Number(seats, 0), 56));
            row.AddChild(Cell(previous == null ? "" : Fmt.Signed(seats - previous.Seats.GetValueOrDefault(party.Id), 0), 50, UiTheme.TextMuted));
            Content.AddChild(row);
        }
    }

    void President(PoliticsSnapshot p, ElectionSnapshot r)
    {
        Content.AddChild(r.Runoff != null ? Header("Кандидат", ("1-й тур", 70), ("2-й тур", 70)) : Header("Кандидат", ("голоса", 70)));
        foreach (var party in p.Parties.OrderByDescending(x => r.Votes.GetValueOrDefault(x.Id)))
        {
            var row = Row(party, r.Candidates.GetValueOrDefault(party.Id));
            row.AddChild(Cell(Fmt.Value("percent", r.Votes.GetValueOrDefault(party.Id) * 100), 70));
            row.AddChild(Cell(r.Runoff?.TryGetValue(party.Id, out var second) == true ? Fmt.Value("percent", second * 100) : "", 70,
                party.Id == r.Winner ? UiTheme.Accent : null));
            Content.AddChild(row);
        }
    }

    static HBoxContainer Row(PartySnapshot party, string? candidate = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Dot(party.Color));
        var name = UiTheme.Text(candidate != null ? $"{candidate} ({party.Short})" : party.Name, 14, null, UiTheme.TextSecondary);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        row.AddChild(name);
        return row;
    }

    static HBoxContainer Header(string first, params (string Text, float Width)[] columns)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });
        var name = UiTheme.Text(first.ToUpperInvariant(), 10, UiTheme.MapCountryFont, UiTheme.TextMuted);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(name);
        foreach (var (text, width) in columns)
        {
            var label = UiTheme.Text(text.ToUpperInvariant(), 10, UiTheme.MapCountryFont, UiTheme.TextMuted);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.CustomMinimumSize = new Vector2(width, 0);
            row.AddChild(label);
        }
        return row;
    }
}
