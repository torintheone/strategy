using Godot;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Text;

namespace Politeconomy.Game.UI;

/// <summary>Итоги правления: сколько лет у власти, как изменились главные показатели, выборы и законы.</summary>
public partial class ReignSummaryDialog : ModalDialog
{
    GameData _data = null!;

    public void Setup(GameData data)
    {
        _data = data;
        InitDialog(560);
        AddButton("Закрыть", true, () => Visible = false);
    }

    public void Show(ReignSummary s, string countryName)
    {
        ClearContent();
        Title.Text = $"Итоги правления: {countryName}";
        var months = (s.Until.Year - s.Since.Year) * 12 + s.Until.Month - s.Since.Month;
        Subtitle.Text = $"С {Fmt.Date(s.Since)} по {Fmt.Date(s.Until)} — {Duration(months)} у власти";
        foreach (var id in PoliticsActions.ReignStats)
        {
            if (!s.Start.TryGetValue(id, out var start) || !s.End.TryGetValue(id, out var end))
                continue;
            var def = _data.Indicator(id);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var name = UiTheme.Text(def.Name, 14, null, UiTheme.TextSecondary);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(Cell(Fmt.Value(def.Format, start, def.Unit), 130, UiTheme.TextMuted));
            row.AddChild(UiTheme.Text("→", 14, null, UiTheme.TextMuted));
            row.AddChild(Cell(Fmt.Value(def.Format, end, def.Unit), 130));
            Content.AddChild(row);
        }
        Content.AddChild(Line($"Выборов выиграно: {s.ElectionsWon}, проиграно: {s.ElectionsLost}. Законов принято: {s.LawsPassed}.", 14, UiTheme.TextPrimary));
        Content.AddChild(Line("Можно выбрать на карте любую страну и начать новую партию за неё.", 13, UiTheme.TextMuted));
        Visible = true;
    }

    static string Duration(int months)
    {
        var years = months / 12;
        var rest = months % 12;
        var parts = new List<string>();
        if (years > 0)
            parts.Add(Fmt.Plural(years, "год", "года", "лет"));
        if (rest > 0 || years == 0)
            parts.Add(Fmt.Plural(rest, "месяц", "месяца", "месяцев"));
        return string.Join(" ", parts);
    }
}
