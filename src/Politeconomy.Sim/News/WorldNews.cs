using System.Text.Json.Serialization;

namespace Politeconomy.Sim.News;

/// <summary>Лента новостей мира, память о том, о каких значениях показателей уже сообщали, телевыпуски и газета. MODEL.md, «Новости», «Телевыпуск», «Газета».</summary>
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

    /// <summary>Телевыпуски страны игрока, новые — в конце; хранятся последние (balance.json → broadcast → keep).</summary>
    public List<Broadcast> Broadcasts { get; set; } = [];
    /// <summary>Ведущий телевыпуска каждой страны, для которой выпуск уже выходил.</summary>
    public SortedDictionary<string, BroadcastAnchor> Anchors { get; set; } = new(StringComparer.Ordinal);
    public int NextBroadcast { get; set; } = 1;
    /// <summary>Номера газеты страны игрока, новые — в конце; хранятся последние (balance.json → newspaper → keep).</summary>
    public List<Newspaper> Papers { get; set; } = [];
    public int NextPaper { get; set; } = 1;
    /// <summary>Когда газета разоблачила министра: «страна/номер персонажа» → дата; «страна» → последнее разоблачение в ней.</summary>
    public SortedDictionary<string, DateOnly> Exposed { get; set; } = new(StringComparer.Ordinal);

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
