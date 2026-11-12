using Politeconomy.Sim.Data;
using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Events;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.News;

/// <summary>
/// Новости. Лента собирается из модели, а не пишется сценарием: раз в месяц — новости о показателях (по шаблонам из
/// data/news.json: показатель ушёл от значения, о котором сообщали в прошлый раз), о мировых ценах; сразу — всё, что
/// случается в странах (записи журнала, решения правительств, события, войны и мир). О своей стране в ленту идёт всё,
/// о чужих — только важное: важность новости + поправка на державу + близость к стране игрока. Новости ничего не
/// меняют в модели. MODEL.md, «Новости».
/// </summary>
public static class NewsModel
{
    /// <summary>Вместо источника: запись журнала новостью не становится.</summary>
    public const string None = "-";

    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.NewsSystem()];

    public static void Setup(World world) => world.News = new WorldNews();

    public static void Ensure(World world) => world.News ??= new WorldNews();

    /// <summary>
    /// Месяц: новости о показателях каждой страны и о мировых ценах. В первый месяц значения только запоминаются.
    /// </summary>
    public static void Month(World world)
    {
        Ensure(world);
        var news = world.News!;
        var data = world.Data.News;
        var prime = !news.Primed;
        foreach (var country in world.Countries)
        {
            if (!news.Marks.TryGetValue(country.Id, out var marks))
                news.Marks[country.Id] = marks = new SortedDictionary<string, double>(StringComparer.Ordinal);
            foreach (var rule in data.Indicators)
            {
                if (!country.Stats.TryGetValue(rule.Stat, out var stat) || !double.IsFinite(stat.Value))
                    continue;
                var value = rule.Round ? Math.Round(stat.Value) : stat.Value;
                if (prime || !marks.TryGetValue(rule.Stat, out var mark))
                {
                    marks[rule.Stat] = value;
                    continue;
                }
                var change = value - mark;
                var step = Math.Max(rule.Step, rule.Relative * Math.Abs(mark));
                // Округляемый показатель (рейтинг, место) не должен «дребезжать» на границе: нужно уйти за неё на четверть шага.
                if (rule.Round ? Math.Abs(stat.Value - mark) < step - 0.25 || value == mark : Math.Abs(change) < step - 1e-9)
                    continue;
                marks[rule.Stat] = value;
                if (Phrase(change > 0 ? rule.Up : rule.Down, value) is not { } phrase)
                    continue;
                var def = world.Data.Indicator(rule.Stat);
                Publish(world, country.Id, rule.Topic, Fill(phrase.Text, def.Format, value, mark), phrase.Weight ?? rule.Weight, phrase.Tone,
                    source: "stat:" + rule.Stat);
            }
        }
        if (world.Economy is { } economy)
            foreach (var rule in data.Prices)
            {
                var price = rule.Price switch { "oil_price" => economy.OilPrice, "ores_price" => economy.OresPrice, "food_price" => economy.FoodPrice, _ => double.NaN };
                if (!double.IsFinite(price) || price <= 0)
                    continue;
                if (prime || !news.Prices.TryGetValue(rule.Price, out var mark) || mark <= 0)
                {
                    news.Prices[rule.Price] = price;
                    continue;
                }
                var change = price / mark - 1;
                if (Math.Abs(change) < rule.Step - 1e-9)
                    continue;
                news.Prices[rule.Price] = price;
                var phrase = change > 0 ? rule.Up : rule.Down;
                Publish(world, null, rule.Topic, phrase.Text.Replace("{change}", Fmt.Number(Math.Abs(change) * 100, 0) + "%"), phrase.Weight ?? rule.Weight,
                    phrase.Tone, source: "price:" + rule.Price);
            }
        news.Primed = true;
    }

    /// <summary>Первая формулировка, чьи границы подходят значению.</summary>
    static NewsPhraseRecord? Phrase(IReadOnlyList<NewsPhraseRecord>? phrases, double value) =>
        phrases?.FirstOrDefault(p => (p.Min is not { } min || value >= min) && (p.Max is not { } max || value <= max));

    /// <summary>
    /// Подстановки: {value} — новое значение в формате показателя, {abs} — оно же без знака, {was} — прежнее,
    /// {change} — на сколько изменилось (без знака).
    /// </summary>
    public static string Fill(string text, string format, double value, double was)
    {
        if (!text.Contains('{'))
            return text;
        var plain = format.Replace("_signed", "");
        return text
            .Replace("{value}", Fmt.Value(format, value))
            .Replace("{abs}", Fmt.Value(plain, Math.Abs(value)))
            .Replace("{was}", Fmt.Value(format, was))
            .Replace("{change}", Fmt.Value(plain, Math.Abs(value - was)));
    }

    /// <summary>
    /// Новость из источника (вид записи журнала или решения страны): рубрика и важность — из data/news.json → sources;
    /// источника там нет — новости нет. Home — касается ли страны игрока (по умолчанию — если это она или её партнёр).
    /// </summary>
    public static NewsItem? Report(World world, string? country, string source, string text, string? partner = null, string? region = null, bool? home = null) =>
        source != None && world.Data.News.Sources.TryGetValue(source, out var s)
            ? Publish(world, country, s.Topic, s.Sentences > 0 ? Sentences(text, s.Sentences) : text, s.Weight, s.Tone, partner, region, home, source)
            : null;

    /// <summary>Первые count предложений текста (подсказки игроку из записи журнала в новость не идут).</summary>
    public static string Sentences(string text, int count)
    {
        var at = -1;
        for (var k = 0; k < count; k++)
        {
            at = text.IndexOf(". ", at + 1, StringComparison.Ordinal);
            if (at < 0)
                return text;
        }
        return text[..(at + 1)];
    }

    /// <summary>Новость о решённом событии: что случилось и что решило правительство; рубрика и важность — по категории события.</summary>
    public static NewsItem? Event(World world, EventRecord record, EventInstance instance, EventNews resolved)
    {
        if (!world.Data.News.Events.TryGetValue(record.Category, out var s))
            return null;
        var decision = resolved.Decision.Length == 0 ? "" : char.ToLowerInvariant(resolved.Decision[0]) + resolved.Decision[1..];
        // Вторая страна события называется в новости, даже если её нет в заголовке («Кибератака: Россия»).
        var title = instance.Partner != null && world.ById(instance.Partner) is { } partner && !resolved.Title.Contains(partner.Name, StringComparison.Ordinal)
            ? $"{resolved.Title}: {partner.Name}"
            : resolved.Title;
        return Publish(world, resolved.Country, s.Topic, $"{title}. Решение: {decision}.", s.Weight, s.Tone, instance.Partner, instance.Region,
            source: "event:" + record.Id);
    }

    /// <summary>
    /// Выпустить новость. О своей стране (или если она — вторая сторона) новость идёт в ленту всегда; чужая — только
    /// если её важность с поправками не ниже WorldMin. Возвращает новость или null, если она не прошла отбор.
    /// </summary>
    public static NewsItem? Publish(World world, string? country, string topic, string text, int weight, string tone = "neutral",
        string? partner = null, string? region = null, bool? home = null, string source = "")
    {
        Ensure(world);
        var news = world.News!;
        var b = world.Data.NewsBalance;
        var player = world.PlayerCountry;
        var mine = home ?? (player != null && (country == player || partner == player));
        var score = Score(world, country, partner, weight, mine);
        var bucket = mine ? "home" : score >= b.WorldMin ? "world" : "skipped";
        news.Counts[bucket] = news.Counts.GetValueOrDefault(bucket) + 1;
        if (bucket == "skipped")
            return null;
        // Заголовок — без точки в конце.
        if (text.EndsWith('.') && !text.EndsWith("..", StringComparison.Ordinal))
            text = text[..^1];
        var item = new NewsItem(news.NextId++, world.Date, country, topic, text, score, tone, mine, partner, region, source);
        news.Items.Add(item);
        Trim(news, mine, mine ? b.HomeKeep : b.WorldKeep);
        news.Version++;
        return item;
    }

    /// <summary>
    /// Важность для игрока: своя страна — + HomeBonus; чужая — + поправка на державу (первая по мощи — RankBonus, к
    /// месту RankSpan — ноль; берётся старшая из двух сторон) + близость к стране игрока (сосед, союзник, противник в
    /// войне, крупный торговый партнёр — наибольшая).
    /// </summary>
    public static int Score(World world, string? country, string? partner, int weight, bool home)
    {
        var b = world.Data.NewsBalance;
        if (home)
            return weight + b.HomeBonus;
        if (country == null)
            return weight;
        var bonus = Math.Max(Rank(world, b, country), partner == null ? 0 : Rank(world, b, partner));
        if (world.PlayerCountry is { } player && world.ById(player) != null)
            bonus += Math.Max(Near(world, b, player, country), partner == null ? 0 : Near(world, b, player, partner));
        return (int)Math.Round(weight + bonus);
    }

    static double Rank(World world, NewsBalance b, string id) =>
        world.ById(id)?.Stats.GetValueOrDefault("power_rank")?.Value is { } rank && double.IsFinite(rank) && b.RankSpan > 0
            ? b.RankBonus * Math.Max(0, 1 - (rank - 1) / b.RankSpan)
            : 0;

    static double Near(World world, NewsBalance b, string player, string other)
    {
        if (other == player || world.Diplomacy is not { } d || world.ById(other) == null)
            return 0;
        var best = 0.0;
        if (d.Neighbors.Contains(WorldDiplomacy.Key(player, other)))
            best = Math.Max(best, b.NeighborBonus);
        if (DiplomacyModel.Allies(world, player, other))
            best = Math.Max(best, b.AllyBonus);
        if (Military.WarModel.WarBetween(world, player, other) != null)
            best = Math.Max(best, b.EnemyBonus);
        if (d.Index.ContainsKey(player) && d.Index.ContainsKey(other) && DiplomacyModel.TradeShare(world, player, other) * 100 >= b.TradeShare)
            best = Math.Max(best, b.TradeBonus);
        return best;
    }

    /// <summary>Лишние старые новости своей части ленты (о своей стране или остальные) удаляются.</summary>
    static void Trim(WorldNews news, bool home, int keep)
    {
        var extra = news.Items.Count(i => i.Home == home) - Math.Max(1, keep);
        for (var i = 0; extra > 0 && i < news.Items.Count;)
            if (news.Items[i].Home == home)
            {
                news.Items.RemoveAt(i);
                extra--;
            }
            else
                i++;
    }
}
