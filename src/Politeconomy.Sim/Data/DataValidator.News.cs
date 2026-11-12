namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    static readonly HashSet<string> NewsTones = ["good", "bad", "neutral"];

    /// <summary>Новости (news.json): рубрики, показатели, цены, источники, события; коэффициенты news в balance.json.</summary>
    static void ValidateNews(GameData data, Action<string, string> add)
    {
        const string file = "news.json";
        var news = data.News;
        var topics = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in news.Topics)
        {
            if (!topics.Add(t.Id))
                add(file, $"рубрика {t.Id} повторяется");
            if (string.IsNullOrWhiteSpace(t.Name))
                add(file, $"рубрика {t.Id}: нет названия");
            if (!IsHexColor(t.Color))
                add(file, $"рубрика {t.Id}: цвет «{t.Color}» — нужен вид #rrggbb");
        }

        void Source(string where, string topic, int weight, string tone)
        {
            if (!topics.Contains(topic))
                add(file, $"{where}: нет рубрики «{topic}»");
            if (weight is < 0 or > 100)
                add(file, $"{where}: важность {weight} вне 0…100");
            if (!NewsTones.Contains(tone))
                add(file, $"{where}: тон «{tone}» — нужно good, bad или neutral");
        }

        void Phrase(string where, NewsPhraseRecord p, int weight, params string[] allowed)
        {
            Source(where, news.Topics.Count > 0 ? news.Topics[0].Id : "", p.Weight ?? weight, p.Tone);
            if (string.IsNullOrWhiteSpace(p.Text))
                add(file, $"{where}: пустой текст");
            if (p.Min is { } min && p.Max is { } max && min > max)
                add(file, $"{where}: min {min} больше max {max}");
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(p.Text ?? "", @"\{[^}]*\}"))
                if (!allowed.Contains(m.Value))
                    add(file, $"{where}: неизвестная подстановка {m.Value}");
        }

        var indicators = data.Indicators.Indicators.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var stats = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in news.Indicators)
        {
            var where = $"показатель {rule.Stat}";
            if (!indicators.Contains(rule.Stat))
                add(file, $"{where}: нет такого показателя в indicators.json");
            if (!stats.Add(rule.Stat))
                add(file, $"{where}: повторяется (у показателя одно правило)");
            if (rule.Step <= 0 || rule.Relative < 0)
                add(file, $"{where}: step должен быть больше нуля, relative — не меньше нуля");
            if (rule.Up is not { Count: > 0 } && rule.Down is not { Count: > 0 })
                add(file, $"{where}: нужна хотя бы одна формулировка в up или down");
            Source(where, rule.Topic, rule.Weight, "neutral");
            foreach (var p in (rule.Up ?? []).Concat(rule.Down ?? []))
                Phrase(where, p, rule.Weight, "{value}", "{abs}", "{was}", "{change}");
        }
        var prices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in news.Prices)
        {
            var where = $"цена {rule.Price}";
            if (!WorldPrices.Contains(rule.Price))
                add(file, $"{where}: нужно oil_price, ores_price или food_price");
            if (!prices.Add(rule.Price))
                add(file, $"{where}: повторяется");
            if (rule.Step is <= 0 or >= 1)
                add(file, $"{where}: step — доля от 0 до 1");
            Source(where, rule.Topic, rule.Weight, "neutral");
            Phrase(where, rule.Up, rule.Weight, "{change}");
            Phrase(where, rule.Down, rule.Weight, "{change}");
        }
        foreach (var (id, s) in news.Sources)
        {
            Source($"источник {id}", s.Topic, s.Weight, s.Tone);
            if (s.Sentences < 0)
                add(file, $"источник {id}: sentences не может быть меньше нуля");
        }
        var categories = data.Events.Categories.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, s) in news.Events)
        {
            if (!categories.Contains(id))
                add(file, $"события: нет категории «{id}» в events.json");
            Source($"события {id}", s.Topic, s.Weight, s.Tone);
        }
        foreach (var c in categories.Where(c => !news.Events.ContainsKey(c)))
            add(file, $"события: у категории «{c}» нет рубрики и важности");

        var b = data.NewsBalance;
        if (b.HomeKeep < 20 || b.WorldKeep < 20)
            add("balance.json", "news: home_keep и world_keep — не меньше 20");
        if (b.WorldMin is < 0 or > 150 || b.HomeBonus < 0 || b.RankBonus < 0 || b.RankSpan <= 0 || b.NeighborBonus < 0 || b.AllyBonus < 0
            || b.EnemyBonus < 0 || b.TradeBonus < 0 || b.TradeShare < 0)
            add("balance.json", "news: поправки не могут быть отрицательными, rank_span — больше нуля");
    }

    static bool IsHexColor(string? s) => s is { Length: 7 } && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
}
