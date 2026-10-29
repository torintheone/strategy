using Politeconomy.Sim.Data;

namespace Politeconomy.Sim.Text;

/// <summary>Тексты для экрана законов: что изменит вариант закона и как к нему отнесутся классы.</summary>
public static class LawText
{
    /// <summary>Строки «что изменится» при переходе от варианта from к варианту to.</summary>
    public static IReadOnlyList<string> EffectLines(GameData data, LawRecord law, string from, string to)
    {
        var a = law.Variant(from).Effects;
        var b = law.Variant(to).Effects;
        var lines = new List<string>();
        foreach (var key in a.Keys.Union(b.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var delta = b.GetValueOrDefault(key) - a.GetValueOrDefault(key);
            if (Math.Abs(delta) < 1e-9 || !data.Laws.Effects.TryGetValue(key, out var effect))
                continue;
            if (key == "monetary_control")
            {
                lines.Add(delta > 0 ? "Ключевую ставку будет ставить правительство" : "Ключевую ставку будет ставить центробанк");
                continue;
            }
            lines.Add($"{effect.Name}: {Value(effect.Unit, delta)}");
        }
        return lines;
    }

    static string Value(string unit, double v) => unit switch
    {
        "п. п." => Fmt.Signed(v, 1) + Fmt.Nbsp + "п." + Fmt.Nbsp + "п.",
        "п. п. в год" => Fmt.Signed(v, 2) + Fmt.Nbsp + "п." + Fmt.Nbsp + "п. в год",
        "доля" => Fmt.Signed(v * 100, 0) + "%",
        "% ВВП" => Fmt.Signed(v, 1) + "%" + Fmt.Nbsp + "ВВП",
        "лет" => Fmt.Signed(v, 1) + Fmt.Nbsp + "года",
        "пункты" => Fmt.Signed(v, 0),
        _ => Fmt.Signed(v, 2),
    };

    /// <summary>Сдвиг отношения каждого класса (порядок classes.json): новый вариант минус нынешний.</summary>
    public static double[] AttitudeShifts(GameData data, LawRecord law, string from, string to)
    {
        var classes = data.Classes.Classes;
        var a = law.Variant(from).Attitudes;
        var b = law.Variant(to).Attitudes;
        return classes.Select(c => b.GetValueOrDefault(c.Id) - a.GetValueOrDefault(c.Id)).ToArray();
    }

    public static string Stage(string stage) => stage switch
    {
        "drafting" => "готовится",
        "passed" => "принят",
        "rejected" => "отклонён",
        "withdrawn" => "отозван",
        _ => stage,
    };
}
