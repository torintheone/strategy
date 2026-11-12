using Politeconomy.Sim.Data;
using Politeconomy.Sim.Events;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Text;

/// <summary>Тексты событий: подстановки в заголовке и описании и строки «что будет» у каждого варианта — прямо из данных, чтобы текст не расходился с моделью.</summary>
public static class EventText
{
    /// <summary>Подстановки: {country}, {leader}, {reporter}, {subject}, {subject_post}, {partner}, {region}.</summary>
    public static string Fill(World world, Country c, EventRecord e, EventInstance i, string text)
    {
        if (!text.Contains('{'))
            return text;
        var g = world.Government?.Countries.GetValueOrDefault(c.Id);
        var posts = world.Data.Government.Posts;
        string Person(string? post) => post != null && g?.Minister(post) is { } m ? m.Name : "министр";
        string Post(string? post) => posts.FirstOrDefault(p => p.Id == post)?.Name.ToLowerInvariant() ?? "министр";
        return text
            .Replace("{country}", c.Name)
            .Replace("{leader}", g?.Leader.Name ?? "глава государства")
            .Replace("{reporter}", Person(e.Reporter))
            .Replace("{subject_post}", Post(i.Subject))
            .Replace("{subject}", Person(i.Subject))
            .Replace("{partner}", i.Partner != null && world.ById(i.Partner) is { } p ? p.Name : "соседняя страна")
            .Replace("{region}", i.Region != null && world.RegionById(i.Region) is { } r ? r.Name : c.Name);
    }

    /// <summary>«на 6 месяцев», «на год», «на 2 года», «на 5 лет», «надолго».</summary>
    public static string Duration(int months)
    {
        if (months >= 240)
            return "надолго";
        if (months % 12 != 0 || months < 12)
            return "на " + Fmt.Plural(months, "месяц", "месяца", "месяцев");
        var years = months / 12;
        return years == 1 ? "на год" : "на " + Fmt.Plural(years, "год", "года", "лет");
    }

    /// <summary>Что будет, если выбрать вариант: разовые последствия и поправки к модели со сроком.</summary>
    public static IReadOnlyList<string> Consequences(GameData data, EventOptionRecord o)
    {
        var lines = new List<string>();
        if (o.Cost > 0)
            lines.Add($"Разовые расходы: {Fmt.Number(o.Cost, 1)}% ВВП (в долг)");
        else if (o.Cost < 0)
            lines.Add($"Разовый доход: {Fmt.Number(-o.Cost, 1)}% ВВП");
        if (o.Effects is { Count: > 0 } effects)
        {
            var parts = new List<string>();
            foreach (var (key, value) in effects.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                if (data.Laws.Effects.TryGetValue(key, out var effect))
                    parts.Add($"{Lower(effect.Name)} {LawText.Value(effect.Unit, value)}");
            if (parts.Count > 0)
                lines.Add($"{Capital(Duration(o.Months))}: {string.Join("; ", parts)}");
        }
        if (o.Moods is { Count: > 0 } moods)
            lines.Add($"Удовлетворённость {Duration(o.Months)}: " + string.Join(", ", data.Classes.Classes.Where(c => moods.ContainsKey(c.Id))
                .Select(c => $"{Lower(c.Name)} {Fmt.Signed(moods[c.Id], 0)}")));
        if (o.Groups is { Count: > 0 } groups)
            lines.Add("Одобрение групп: " + string.Join(", ", data.InterestGroups.Groups.Where(g => groups.ContainsKey(g.Id))
                .Select(g => $"{Lower(g.Name)} {Fmt.Signed(groups[g.Id], 0)}")));
        if (o.Popularity != 0)
            lines.Add($"Популярность главы: {Fmt.Signed(o.Popularity, 0)}");
        if (o.Loyalty is { Count: > 0 } loyalty)
            lines.Add("Лояльность: " + string.Join(", ", loyalty.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{(kv.Key == "army" ? "армия" : "полиция")} {Fmt.Signed(kv.Value, 0)}")));
        if (o.Relations is { } relations)
            lines.Add($"Отношения {RelationsWith(relations.With)}: {Fmt.Signed(relations.Value, 0)}");
        if (o.Subject == "fire")
            lines.Add("Министр уходит в отставку");
        else if (double.TryParse(o.Subject, System.Globalization.CultureInfo.InvariantCulture, out var shift) && shift != 0)
            lines.Add($"Лояльность министра: {Fmt.Signed(shift, 0)}");
        if (o.Next is { Count: > 0 })
            lines.Add("У этого решения может быть продолжение");
        if (lines.Count == 0)
            lines.Add("Заметных последствий нет");
        return lines;
    }

    public static string RelationsWith(string with) => with switch
    {
        "partner" => "с этой страной",
        "neighbors" => "с соседями",
        "powers" => "с ведущими державами",
        _ => "со всеми странами",
    };

    static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
