using System.Text.Json.Serialization;

namespace Politeconomy.Sim.News;

/// <summary>Лента новостей мира и память о том, о каких значениях показателей уже сообщали. MODEL.md, «Новости».</summary>
public sealed class WorldNews
{
    /// <summary>Новости, новые — в конце. Хранятся последние: отдельно о своей стране, отдельно остальные.</summary>
    public List<NewsItem> Items { get; set; } = [];
    /// <summary>О каком значении показателя сообщали в последний раз: страна → показатель → значение.</summary>
    public SortedDictionary<string, SortedDictionary<string, double>> Marks { get; set; } = new(StringComparer.Ordinal);
    /// <summary>То же для мировых цен.</summary>
    public SortedDictionary<string, double> Prices { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Первый месяц только запоминает показатели: модель в нём ещё переходит от данных к своим расчётам.</summary>
    public bool Primed { get; set; }
    public int NextId { get; set; } = 1;
    /// <summary>Сколько новостей вышло за всю игру: home — о своей стране, world — попавших в ленту чужих, skipped — не прошедших отбор.</summary>
    public SortedDictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Растёт с каждой новостью: по нему интерфейс понимает, что лента изменилась (не сохраняется).</summary>
    [JsonIgnore] public int Version { get; set; }
    /// <summary>Готовый снимок ленты для интерфейса — пересобирается, только когда лента изменилась.</summary>
    [JsonIgnore] public Engine.NewsSnapshot? Snapshot { get; set; }
}

/// <summary>
/// Новость: когда, о какой стране (null — о мире в целом), рубрика, текст без названия страны, важность для игрока
/// (с поправками на державу, близость и «свою страну»), тон (good, bad, neutral), касается ли страны игрока, вторая
/// страна и регион (если есть), источник (какое правило или вид записи её породили).
/// </summary>
public sealed record NewsItem(int Id, DateOnly Date, string? Country, string Topic, string Text, int Weight, string Tone = "neutral",
    bool Home = false, string? Partner = null, string? Region = null, string Source = "");
