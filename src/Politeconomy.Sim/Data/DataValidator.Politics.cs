namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial System.Text.RegularExpressions.Regex YearMonth();

    static readonly HashSet<string> ElectoralSystems = ["majoritarian", "proportional", "mixed", "none"];
    static readonly HashSet<string> Genders = ["m", "f", "n"];

    /// <summary>Партии (parties.json), избирательные системы (politics.json), взгляды классов и коэффициенты раздела politics.</summary>
    static void ValidatePolitics(GameData data, Action<string, string> add)
    {
        var parties = data.Parties;
        if (parties.Axes.Count != 3)
            add("parties.json", $"осей взглядов {parties.Axes.Count}, нужно 3");
        var families = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in parties.Families.Append(parties.Power))
        {
            if (!families.Add(f.Id))
                add("parties.json", $"семейство {f.Id} встречается дважды");
            if (f.Position.Count != 3 || f.Position.Any(x => x is < -100 or > 100))
                add("parties.json", $"{f.Id}: позиция — три числа от −100 до 100");
            if (!HexColor().IsMatch(f.Color))
                add("parties.json", $"{f.Id}: цвет «{f.Color}» не в формате #rrggbb");
            if (f.Adjectives.Any(a => a.Count != 3))
                add("parties.json", $"{f.Id}: у прилагательного должно быть три рода");
            if (f.Adjectives.Count + f.Tails.Count + f.Quoted.Count == 0)
                add("parties.json", $"{f.Id}: нет частей названия");
            if (f.ByIncome is { Count: not 4 })
                add("parties.json", $"{f.Id}: by_income — четыре числа по группам дохода");
            if (f.Weight < 0)
                add("parties.json", $"{f.Id}: вес не может быть отрицательным");
        }
        if (parties.Nouns.Count == 0)
            add("parties.json", "нет слов для названий партий");
        foreach (var n in parties.Nouns.Where(n => !Genders.Contains(n.Gender) || n.Weight <= 0))
            add("parties.json", $"слово «{n.Word}»: род m, f или n и вес больше нуля");

        foreach (var c in data.Politics.Countries)
        {
            if (!ElectoralSystems.Contains(c.Electoral))
                add("politics.json", $"{c.Id}: избирательная система «{c.Electoral}» — нужно majoritarian, proportional, mixed или none");
            if (c.Threshold is < 0 or > 20)
                add("politics.json", $"{c.Id}: барьер {c.Threshold}% вне 0…20");
            if (c.Parties && c.Electoral == "none")
                add("politics.json", $"{c.Id}: партии есть, а выборов нет");
            if (c.RulingFamily != null && !families.Contains(c.RulingFamily))
                add("politics.json", $"{c.Id}: семейства «{c.RulingFamily}» нет в parties.json");
            if (c.Term is < 1 or > 10 || c.PresidentTerm is < 1 or > 10)
                add("politics.json", $"{c.Id}: срок парламента и президента — от 1 до 10 лет");
            foreach (var date in new[] { c.NextElection, c.NextPresident }.Where(d => d != null))
                if (!YearMonth().IsMatch(date!))
                    add("politics.json", $"{c.Id}: дата выборов «{date}» — нужно «год-месяц», например 2026-10");
            if (c.NextPresident != null && c.PresidentTerm == null)
                add("politics.json", $"{c.Id}: есть дата президентских выборов, но нет срока president_term");
            if (c.PresidentTerm != null && c.System is not ("presidential_republic" or "semi_presidential"))
                add("politics.json", $"{c.Id}: президентские выборы бывают только в президентской или смешанной республике");
        }

        foreach (var c in data.Classes.Classes)
        {
            if (c.Views is not { Count: 3 } || c.Views.Any(v => v is < -100 or > 100))
                add("classes.json", $"{c.Id}: views — три числа от −100 до 100");
            if (c.Turnout is < 0.05 or > 1)
                add("classes.json", $"{c.Id}: turnout — явка от 0,05 до 1");
        }

        var b = data.PoliticsBalance;
        if (b.DistanceScale <= 0 || b.ViewMonths < 1 || b.PollMonths < 1)
            add("balance.json", "politics: distance_scale > 0, view_months и poll_months не меньше 1");
        if (b.Salience.Count != 3 || b.CrisisShift.Count != 3)
            add("balance.json", "politics: salience и crisis_shift — по три числа (оси)");
        if (b.AdminBonus.Count != 3 || b.RegimeCivil.Count != 3 || b.RegimeForeign.Count != 3)
            add("balance.json", "politics: admin_bonus, regime_civil, regime_foreign — по три числа (режимы)");
        foreach (var range in new[] { b.MajoritarianParties, b.ProportionalParties, b.MixedParties, b.HybridParties, b.AuthoritarianParties, b.OnePartyParties })
            if (range.Count != 2 || range[0] < 1 || range[1] < range[0])
                add("balance.json", "politics: число партий — «от, до», от 1");
        if (b.ProportionalParties[1] > parties.Families.Count || b.MixedParties[1] > parties.Families.Count)
            add("balance.json", "politics: партий больше, чем семейств в parties.json");
    }
}
