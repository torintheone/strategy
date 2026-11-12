namespace Politeconomy.Sim.Data;

// Новости. Поля описаны в data/README.md, смысл — в MODEL.md, «Новости».

/// <summary>
/// data/news.json: рубрики, новости о показателях (шаблоны по изменению показателя), новости о мировых ценах,
/// источники (какие записи журнала и решения стран становятся новостями и насколько они важны) и события по категориям.
/// </summary>
public sealed record NewsFile(
    IReadOnlyList<NewsTopicRecord> Topics,
    IReadOnlyList<NewsIndicatorRecord> Indicators,
    IReadOnlyList<NewsPriceRecord> Prices,
    IReadOnlyDictionary<string, NewsSourceRecord> Sources,
    IReadOnlyDictionary<string, NewsSourceRecord> Events)
{
    public NewsTopicRecord? Topic(string id) => Topics.FirstOrDefault(t => t.Id == id);
}

/// <summary>Рубрика: название и цвет метки в ленте.</summary>
public sealed record NewsTopicRecord(string Id, string Name, string Color);

/// <summary>
/// Новость о показателе. Модель помнит значение, о котором сообщала в последний раз; когда показатель уходит от него
/// на Step (или на долю Relative от него, если это больше) вверх или вниз — выходит новость и запоминается новое
/// значение. Round — сравнивать округлённые значения (рейтинг, место). Up и Down — формулировки для роста и падения:
/// берётся первая, чьи Min/Max подходят новому значению; если не подошла ни одна, новость не выходит.
/// </summary>
public sealed record NewsIndicatorRecord(
    string Stat,
    double Step,
    string Topic,
    int Weight,
    IReadOnlyList<NewsPhraseRecord>? Up = null,
    IReadOnlyList<NewsPhraseRecord>? Down = null,
    double Relative = 0,
    bool Round = false);

/// <summary>
/// Формулировка: текст с подстановками {value} (новое значение), {was} (прежнее), {change} (на сколько изменилось),
/// тон (good, bad, neutral), при каких значениях годится (Min/Max), своя важность (иначе — важность показателя).
/// </summary>
public sealed record NewsPhraseRecord(string Text, string Tone = "neutral", double? Min = null, double? Max = null, int? Weight = null);

/// <summary>
/// Новость о мировой цене (oil_price, ores_price, food_price): выходит, когда цена изменилась на долю Step с прошлого
/// сообщения. В тексте {change} — на сколько процентов.
/// </summary>
public sealed record NewsPriceRecord(string Price, double Step, string Topic, int Weight, NewsPhraseRecord Up, NewsPhraseRecord Down);

/// <summary>Источник новости: рубрика, важность (0–100), тон; Sentences — сколько первых предложений записи взять в новость (0 — весь текст).</summary>
public sealed record NewsSourceRecord(string Topic, int Weight, string Tone = "neutral", int Sentences = 0);

// Коэффициенты новостей из data/balance.json (раздел news).

public sealed class NewsBalance
{
    /// <summary>Сколько новостей о своей стране и сколько остальных хранить (старые удаляются).</summary>
    public int HomeKeep { get; init; } = 150;
    public int WorldKeep { get; init; } = 250;
    /// <summary>Чужая новость попадает в ленту, если её важность с поправками не ниже WorldMin.</summary>
    public int WorldMin { get; init; } = 60;
    /// <summary>Новость о своей стране важнее на HomeBonus.</summary>
    public int HomeBonus { get; init; } = 25;
    /// <summary>Державы: первая по мощи получает RankBonus, дальше поправка убывает до нуля к месту RankSpan.</summary>
    public double RankBonus { get; init; } = 30;
    public double RankSpan { get; init; } = 40;
    /// <summary>Близость к стране игрока: сосед, союзник, противник в войне, крупный торговый партнёр (доля от TradeShare, %). Берётся наибольшая.</summary>
    public double NeighborBonus { get; init; } = 15;
    public double AllyBonus { get; init; } = 10;
    public double EnemyBonus { get; init; } = 20;
    public double TradeBonus { get; init; } = 10;
    public double TradeShare { get; init; } = 5;
}
