using System.Text.Json.Serialization;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Events;

/// <summary>
/// События всех стран и свой генератор случайных чисел (как у политики: события не должны сбивать случайные толчки
/// экономики). MODEL.md, «События».
/// </summary>
public sealed class WorldEvents
{
    public SortedDictionary<string, CountryEvents> Countries { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Последние события мира с решениями (для новостей), новые — в конце.</summary>
    public List<EventNews> Recent { get; set; } = [];
    /// <summary>Сколько раз случилось каждое событие за всю игру.</summary>
    public SortedDictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);
    public int NextId { get; set; } = 1;

    DeterministicRandom? _random;
    ulong[] _state = [];

    /// <summary>Состояние генератора событий (для сохранения).</summary>
    public ulong[] Rng
    {
        get => _random?.State ?? _state;
        set => (_state, _random) = (value, null);
    }

    [JsonIgnore]
    public DeterministicRandom Random => _random ??= _state.Length == 4 ? DeterministicRandom.FromState(_state) : new DeterministicRandom(0);

    public static WorldEvents Create(ulong seed) => new() { _random = new DeterministicRandom(seed) };

    public CountryEvents this[string country] => Countries[country];
}

/// <summary>События страны: когда что случалось, что ждёт решения игрока, какие последствия ещё действуют, что придёт позже.</summary>
public sealed class CountryEvents
{
    /// <summary>Когда событие случалось в последний раз (для перезарядки).</summary>
    public SortedDictionary<string, DateOnly> Last { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Когда в стране случилось последнее событие (любое).</summary>
    public DateOnly? LastAny { get; set; }
    /// <summary>События, которые ждут решения игрока.</summary>
    public List<EventInstance> Pending { get; set; } = [];
    /// <summary>Действующие последствия решений: до какой даты.</summary>
    public List<ActiveEffect> Active { get; set; } = [];
    /// <summary>Продолжения цепочек: что и когда придёт.</summary>
    public List<EventInstance> Scheduled { get; set; } = [];
    /// <summary>Последние решённые события страны, новые — в конце.</summary>
    public List<EventNews> History { get; set; } = [];
    /// <summary>Сумма действующих поправок к модели (ключи — как у законов).</summary>
    public Dictionary<string, double> Effects { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Сумма действующих сдвигов удовлетворённости классов.</summary>
    public double[] Mood { get; set; } = [];
    public int Version { get; set; }
}

/// <summary>
/// Событие, случившееся в стране: что (Event), когда, до какого дня игрок должен решить; «герой» (пост министра и его
/// номер — если министра сменили, событие его уже не касается), страна-партнёр, регион; почему случилось (словами).
/// Для продолжения цепочки Date — когда оно придёт.
/// </summary>
public sealed class EventInstance
{
    public int Id { get; set; }
    public string Event { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateOnly Deadline { get; set; }
    public string? Subject { get; set; }
    public int SubjectId { get; set; }
    public string? Partner { get; set; }
    public string? Region { get; set; }
    /// <summary>Событие, продолжением которого это стало (для цепочек).</summary>
    public string? Parent { get; set; }
    public List<string> Why { get; set; } = [];
}

/// <summary>Действующие последствия варианта Option события Event — до даты Until.</summary>
public sealed record ActiveEffect(string Event, string Option, DateOnly Since, DateOnly Until);

/// <summary>Событие с решением — для новостей и истории: когда, где, что случилось (заголовок) и что решили.</summary>
public sealed record EventNews(DateOnly Date, string Country, string Event, string Option, string Title, string Decision, string? Region = null);
