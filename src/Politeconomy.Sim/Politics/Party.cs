using System.Text.Json.Serialization;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Партия: вымышленное название в местном стиле, семейство из data/parties.json, позиция на трёх осях взглядов
/// (−100…100), лидер, организованность (0–1), доля в опросах (0–1) и состав её избирателей по классам.
/// MODEL.md, «Партии».
/// </summary>
public sealed class Party
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Короткое название для графиков и легенд.</summary>
    public string Short { get; set; } = "";
    public string Family { get; set; } = "";
    public string Color { get; set; } = "";
    public double[] Position { get; set; } = new double[3];
    /// <summary>Лидер партии; null — партию возглавляет глава исполнительной власти (CountryGovernment.Leader).</summary>
    public Character? Leader { get; set; }
    public double Organization { get; set; }
    public int Founded { get; set; }
    /// <summary>Входит в правящую коалицию.</summary>
    public bool Ruling { get; set; }
    /// <summary>С какого времени партия у власти (для «усталости от власти»); null — в оппозиции.</summary>
    public DateOnly? RulingSince { get; set; }
    /// <summary>Случайный дрейф привлекательности: скандалы, удачи, медиа. Медленно возвращается к нулю.</summary>
    public double Momentum { get; set; }
    /// <summary>Партия власти гибридного или авторитарного режима: у неё административный ресурс.</summary>
    public bool Power { get; set; }
    /// <summary>Лояльная («системная») оппозиция авторитарного режима.</summary>
    public bool Loyal { get; set; }
    /// <summary>Доля в опросах среди определившихся, 0–1.</summary>
    public double Poll { get; set; }
    /// <summary>Из кого состоят избиратели партии: доли классов (порядок data/classes.json), сумма 1.</summary>
    public double[] Voters { get; set; } = new double[ClassIds.Count];
    /// <summary>Прибавка к привлекательности от кампании у каждого класса (поездки, агитация, обещания, дебаты); затухает.</summary>
    public double[] Campaign { get; set; } = new double[ClassIds.Count];
}

/// <summary>Опрос месяца: доли партий (id → 0–1), как их видит публика (с погрешностью).</summary>
public sealed record PollRecord(DateOnly Date, Dictionary<string, double> Shares);

/// <summary>
/// Политика страны: сдвиг её взглядов от средних по миру, нынешние взгляды классов, партии и история опросов.
/// </summary>
public sealed class CountryPolitics
{
    /// <summary>Сдвиг средних взглядов страны по трём осям (задаётся на старте и не меняется).</summary>
    public double[] Offset { get; set; } = new double[3];
    /// <summary>Нынешние взгляды классов: [класс][ось].</summary>
    public double[][] Views { get; set; } = [];
    public List<Party> Parties { get; set; } = [];
    /// <summary>Ежемесячные опросы, новые в конце.</summary>
    public List<PollRecord> Polls { get; set; } = [];
    /// <summary>Ближайшие выборы парламента и президента (null — таких выборов нет).</summary>
    public DateOnly? NextElection { get; set; }
    public DateOnly? NextPresident { get; set; }
    /// <summary>Итоги последних выборов, новые в конце.</summary>
    public List<ElectionResult> Elections { get; set; } = [];
    /// <summary>Переговоры о коалиции (только в стране игрока).</summary>
    public CoalitionTalks? Talks { get; set; }
    /// <summary>Обещания партнёрам по коалиции.</summary>
    public List<CoalitionPromise> Promises { get; set; } = [];
    /// <summary>Предвыборная кампания партии игрока (null — кампании нет).</summary>
    public Campaign? Campaign { get; set; }
    /// <summary>Обещания избирателям, данные в кампании.</summary>
    public List<CampaignPromise> Pledges { get; set; } = [];
    /// <summary>Скандал после разоблачённых фальсификаций: минус к привлекательности правящих, плюс к радикализму; затихает.</summary>
    public double Scandal { get; set; }
    /// <summary>Номер для id следующей новой партии.</summary>
    public int NextParty { get; set; } = 1;
    /// <summary>Растёт при каждом изменении — по нему снимок понимает, что пора пересобраться.</summary>
    public int Version { get; set; }

    public Party? Party(string? id) => id == null ? null : Parties.FirstOrDefault(p => p.Id == id);
}

/// <summary>
/// Политика всех стран и свой генератор случайных чисел: итоги выборов в разных мирах расходятся, и если бы
/// политика брала числа из общего генератора, вслед за ней разошлись бы и случайные толчки экономики.
/// </summary>
public sealed class WorldPolitics
{
    public Dictionary<string, CountryPolitics> Countries { get; set; } = new(StringComparer.Ordinal);

    DeterministicRandom? _random;
    ulong[] _state = [];

    /// <summary>Состояние генератора политики (для сохранения).</summary>
    public ulong[] Rng
    {
        get => _random?.State ?? _state;
        set => (_state, _random) = (value, null);
    }

    [JsonIgnore]
    public DeterministicRandom Random => _random ??= _state.Length == 4 ? DeterministicRandom.FromState(_state) : new DeterministicRandom(0);

    public static WorldPolitics Create(ulong seed) => new() { _random = new DeterministicRandom(seed) };

    public CountryPolitics this[string country] => Countries[country];
}
