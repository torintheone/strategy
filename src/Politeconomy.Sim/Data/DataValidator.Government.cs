using System.Text.RegularExpressions;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    static readonly HashSet<string> ParliamentKinds = ["real", "formal", "none"];

    [GeneratedRegex(@"^[А-Яа-яЁё’ \-]+$")]
    private static partial Regex CyrillicName();

    /// <summary>Правительство (government.json), имена (names/*.json) и коэффициенты раздела government.</summary>
    static void ValidateGovernment(GameData data, Action<string, string> add)
    {
        var g = data.Government;
        foreach (var system in Enum.GetValues<PoliticalSystem>().Select(WorldFactory.SystemId))
            if (g.Systems.All(s => s.Id != system))
                add("government.json", $"нет описания системы {system}");
        foreach (var s in g.Systems.Where(s => !ParliamentKinds.Contains(s.Parliament)))
            add("government.json", $"{s.Id}: вид парламента «{s.Parliament}» — нужно real, formal или none");

        var ids = data.Countries.Countries.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in g.LeaderTitles.Keys.Concat(g.ParliamentSeats.Keys).Where(id => !ids.Contains(id)))
            add("government.json", $"страна {id} не найдена в countries.json");
        foreach (var (id, seats) in g.ParliamentSeats.Where(kv => kv.Value is < 5 or > 5000))
            add("government.json", $"{id}: мест в парламенте {seats} — вне 5…5000");

        var spending = data.Budget.Spending.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var posts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in g.Posts)
        {
            if (!posts.Add(p.Id))
                add("government.json", $"пост {p.Id} встречается дважды");
            foreach (var s in p.Spending.Where(s => !spending.Contains(s)))
                add("government.json", $"пост {p.Id}: статьи расходов «{s}» нет в budget.json");
            foreach (var s in (p.Stats ?? []).Where(s => data.Indicators.Indicators.All(i => i.Id != s)))
                add("government.json", $"пост {p.Id}: показателя «{s}» нет в indicators.json");
        }
        if (g.Posts.Count != 18)
            add("government.json", $"постов в правительстве {g.Posts.Count}, по ТЗ — 18");
        var traits = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in g.Traits)
        {
            if (!traits.Add(t.Id))
                add("government.json", $"черта {t.Id} встречается дважды");
            if (t.Weight <= 0)
                add("government.json", $"черта {t.Id}: вес должен быть больше нуля");
        }

        // --- history.json
        var h = data.History;
        if (h.LastYear < h.FirstYear)
            add("history.json", "последний год раньше первого");
        foreach (var (country, rows) in h.Countries)
        {
            if (!ids.Contains(country))
                add("history.json", $"страна {country} не найдена в countries.json");
            foreach (var (key, row) in rows)
            {
                if (row.Count != h.LastYear - h.FirstYear + 1)
                    add("history.json", $"{country}.{key}: значений не столько, сколько лет");
                if (data.Indicators.Indicators.All(i => i.Id != key))
                    add("history.json", $"{country}: показателя «{key}» нет в indicators.json");
            }
        }

        // --- names/*.json: у каждой страны ровно одна культура, имена — кириллицей
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in data.Cultures)
        {
            var file = $"names/{c.Id}.json";
            if (c.Order is not ("given_family" or "family_given"))
                add(file, $"порядок имени «{c.Order}» — нужно given_family или family_given");
            if (c.Male.Count < 5 || c.Female.Count < 5 || c.Surnames.Count < 10)
                add(file, "мало имён: нужно хотя бы 5 мужских, 5 женских и 10 фамилий");
            foreach (var n in c.Male.Concat(c.Female).Concat(c.Surnames).Where(n => !CyrillicName().IsMatch(n)))
                add(file, $"имя «{n}» — нужны русские буквы");
            if (c.Feminine.Any(rule => rule.Count != 2))
                add(file, "правило женской фамилии — пара «окончание, замена»");
            foreach (var country in c.Countries)
            {
                if (!ids.Contains(country))
                    add(file, $"страна {country} не найдена в countries.json");
                else if (!owner.TryAdd(country, c.Id))
                    add(file, $"страна {country} уже есть в культуре {owner[country]}");
            }
        }
        foreach (var id in ids.Where(id => !owner.ContainsKey(id)))
            add("names", $"нет культуры имён для страны {id}");

        // --- balance.json, раздел government
        var b = data.GovernmentBalance;
        if (data.Balance.Government is null)
            add("balance.json", "нет раздела government с коэффициентами правительства");
        if (b.LeaderAge.Count != 2 || b.MinisterAge.Count != 2 || b.LeaderAge[0] >= b.LeaderAge[1] || b.MinisterAge[0] >= b.MinisterAge[1])
            add("balance.json", "government: возраст — пара «от, до», и «от» меньше «до»");
        var parliament = b.Parliament;
        foreach (var (name, range) in new[] { ("democracy_ruling", parliament.DemocracyRuling), ("presidential_ruling", parliament.PresidentialRuling),
                     ("hybrid_ruling", parliament.HybridRuling), ("authoritarian_ruling", parliament.AuthoritarianRuling) })
            if (range.Count != 2 || range[0] < 0 || range[1] > 1 || range[0] > range[1])
                add("balance.json", $"government.parliament.{name}: пара долей от 0 до 1, «от» не больше «до»");
        if (b.Candidates < 1)
            add("balance.json", "government.candidates: хотя бы один кандидат");
    }
}
