namespace Politeconomy.Sim.Data;

// События. Поля описаны в data/README.md, смысл — в MODEL.md, «События».

/// <summary>
/// data/events.json: категории, природные угрозы по странам (уровень 1–3; HazardNames — как угроза называется в
/// объяснении события) и сами события. Движок событий — данные, а не код: условия, вес, перезарядка, варианты
/// с последствиями, цепочки.
/// </summary>
public sealed record EventsFile(
    IReadOnlyList<EventCategoryRecord> Categories,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Hazards,
    IReadOnlyList<EventRecord> Events,
    IReadOnlyDictionary<string, string>? HazardNames = null)
{
    public EventRecord? Event(string id) => Events.FirstOrDefault(e => e.Id == id);
    public EventCategoryRecord? Category(string id) => Categories.FirstOrDefault(c => c.Id == id);
}

public sealed record EventCategoryRecord(string Id, string Name, string Color);

/// <summary>
/// Событие. When — условия (все должны выполняться); Weight — сколько раз в год событие случалось бы, пока условия
/// выполняются (до перезарядки); Scale — как вес растёт с показателем; CooldownMonths — перезарядка в стране; Reporter —
/// пост министра-докладчика; Chain — событие приходит только как продолжение другого (само не случается).
/// В Title и Text можно писать {country}, {leader}, {reporter}, {subject}, {subject_post}, {partner}, {region}.
/// </summary>
public sealed record EventRecord(
    string Id,
    string Category,
    string Title,
    string Text,
    string Reporter,
    IReadOnlyList<EventCondition> When,
    double Weight,
    int CooldownMonths,
    IReadOnlyList<EventOptionRecord> Options,
    EventScale? Scale = null,
    bool Chain = false,
    string? Image = null)
{
    public EventOptionRecord? Option(string id) => Options.FirstOrDefault(o => o.Id == id);
}

/// <summary>
/// Условие (предикат). Заполнено ровно одно «что проверяем»:
/// Stat — показатель страны (Min/Max); World — мир: oil_price, ores_price, food_price (1 — обычный уровень; Min/Max);
/// Law — закон, вариант из In; Regime, Income, System — страна из списка; Tech — технология освоена (Is);
/// Program — программа включена (Is); Class — класс, What: satisfaction или radicalism (Min/Max);
/// Group — группа интересов, её одобрение (Min/Max); Funding — статья расходов: план / план на старте (Min/Max);
/// Minister — пост или any, What: corruption, competence, loyalty, popularity (Min/Max) — найденный министр становится
/// «героем» события ({subject}); Partner — другая страна по условиям, становится {partner};
/// Hazard — природная угроза есть у страны (её уровень умножает вес); War — страна воюет (Is);
/// Any — выполняется хотя бы одно из вложенных условий.
/// </summary>
public sealed record EventCondition(
    string? Stat = null,
    string? World = null,
    string? Law = null,
    IReadOnlyList<string>? In = null,
    IReadOnlyList<string>? Regime = null,
    IReadOnlyList<string>? Income = null,
    IReadOnlyList<string>? System = null,
    string? Tech = null,
    string? Program = null,
    string? Class = null,
    string? Group = null,
    string? Funding = null,
    string? Minister = null,
    EventPartner? Partner = null,
    string? Hazard = null,
    bool? War = null,
    IReadOnlyList<EventCondition>? Any = null,
    string? What = null,
    double? Min = null,
    double? Max = null,
    bool? Is = null);

/// <summary>
/// Какую страну событие берёт в партнёры: сосед (Neighbor), отношения (RelationsMin/RelationsMax), доля в нашей
/// торговле, % (TradeMin), место по мощи не ниже (RankMax), воюет ли она (War), было ли у неё за последние два месяца
/// событие категории Recent (например, disaster). Из подходящих — с самой большой долей в торговле.
/// </summary>
public sealed record EventPartner(bool? Neighbor = null, double? RelationsMin = null, double? RelationsMax = null, double? TradeMin = null, int? RankMax = null,
    bool? War = null, string? Recent = null);

/// <summary>Вес растёт с показателем: × 1 при значении From, × Factor при значении To (между ними — линейно, дальше — не меняется).</summary>
public sealed record EventScale(string Stat, double From, double To, double Factor);

/// <summary>
/// Вариант решения. Months — сколько месяцев действуют Effects (ключи поправок модели, как у законов) и Moods
/// (удовлетворённость классов, пункты). Сразу: Cost (% ВВП — в долг; отрицательный — доход), Popularity (главы),
/// Groups (одобрение групп интересов), Loyalty (police, army), Relations, Subject (fire — уволить «героя»; число —
/// сдвиг его лояльности), Next (продолжения). Requires — условия, без которых вариант закрыт (RequiresText — почему).
/// Ai — насколько вариант служит приоритетам ИИ: рост, стабильность, сила, переизбрание. Default — вариант, который
/// сработает, если игрок не решит в срок.
/// </summary>
public sealed record EventOptionRecord(
    string Id,
    string Name,
    string Text,
    int Months = 12,
    IReadOnlyDictionary<string, double>? Effects = null,
    IReadOnlyDictionary<string, double>? Moods = null,
    IReadOnlyDictionary<string, double>? Groups = null,
    IReadOnlyDictionary<string, double>? Loyalty = null,
    double Cost = 0,
    double Popularity = 0,
    EventRelations? Relations = null,
    string? Subject = null,
    IReadOnlyList<EventNext>? Next = null,
    IReadOnlyList<EventCondition>? Requires = null,
    string? RequiresText = null,
    IReadOnlyList<double>? Ai = null,
    bool Default = false);

/// <summary>Сдвиг отношений: With — partner (партнёр события), neighbors (соседи), powers (первые пять по мощи), all (все).</summary>
public sealed record EventRelations(string With, double Value);

/// <summary>Продолжение: событие Event придёт через Months месяцев с вероятностью Chance (если его условия выполняются).</summary>
public sealed record EventNext(string Event, int Months, double Chance = 1);

// Коэффициенты событий из data/balance.json (раздел events).

public sealed class EventsBalance
{
    /// <summary>Общий множитель весов: больше — события чаще.</summary>
    public double Rate { get; init; } = 1;
    /// <summary>Дни месяца, когда события могут случиться (не больше одного события на страну в такой день).</summary>
    public IReadOnlyList<int> Days { get; init; } = [1, 8, 15, 22];
    /// <summary>После любого события в стране следующее — не раньше чем через QuietDays дней.</summary>
    public int QuietDays { get; init; } = 10;
    /// <summary>Игрок решает событие за DeadlineDays дней; потом срабатывает вариант по умолчанию.</summary>
    public int DeadlineDays { get; init; } = 30;
    /// <summary>ИИ в долговом кризисе вычитает из пользы варианта DebtAversion × его цену (% ВВП).</summary>
    public double DebtAversion { get; init; } = 0.5;
    /// <summary>Сколько последних событий мира помнить для новостей.</summary>
    public int Recent { get; init; } = 300;
}
