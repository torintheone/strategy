using Politeconomy.Sim.Model;
using System.Text.Json.Serialization;

namespace Politeconomy.Sim.Diplomacy;

/// <summary>
/// Дипломатия мира. Memory — память о событиях по парам стран («AAA|BBB», коды по алфавиту), Treaties — договоры,
/// Visits — когда глава последний раз ездил к партнёру. Поправки к модели по странам — в Countries. Торговля пар и
/// отношения не сохраняются: они пересчитываются из данных и состояния. MODEL.md, «Дипломатия».
/// </summary>
public sealed class WorldDiplomacy
{
    public SortedDictionary<string, double> Memory { get; set; } = new(StringComparer.Ordinal);
    public List<Treaty> Treaties { get; set; } = [];
    public SortedDictionary<string, DateOnly> Visits { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, CountryDiplomacy> Countries { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// Строй, партия главы и её взгляды, по которым посчитаны отношения. Обновляются 1-го числа и в день смены власти;
    /// сохраняются, чтобы после загрузки отношения совпали до последнего знака.
    /// </summary>
    public SortedDictionary<string, Stance> Stances { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Члены блоков: id блока → коды стран по алфавиту.</summary>
    public SortedDictionary<string, List<string>> Blocs { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Санкции одной страны против другой.</summary>
    public List<Sanction> Sanctions { get; set; } = [];
    /// <summary>Страны под санкциями Совета Наций (их соблюдают все члены).</summary>
    public List<string> CouncilSanctions { get; set; } = [];
    /// <summary>Выборные места Совета безопасности.</summary>
    public List<CouncilSeat> Seats { get; set; } = [];
    /// <summary>Последние резолюции Совета Наций.</summary>
    public List<Resolution> Resolutions { get; set; } = [];
    /// <summary>Когда страна последний раз была выборным членом Совета безопасности (год окончания срока).</summary>
    public SortedDictionary<string, int> Served { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Когда страна последний раз выносила резолюцию.</summary>
    public SortedDictionary<string, DateOnly> Proposed { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Программы Валютного совета по странам.</summary>
    public SortedDictionary<string, FundProgram> Programs { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Порядок стран, доли торговли и отношения — пересчитываются (не сохраняются).</summary>
    [JsonIgnore] public Dictionary<string, int> Index { get; set; } = new(StringComparer.Ordinal);
    [JsonIgnore] public string[] Ids { get; set; } = [];
    [JsonIgnore] public double[][] Trade { get; set; } = [];
    [JsonIgnore] public double[][] Relations { get; set; } = [];
    /// <summary>Пары соседей по суше и история пар («AAA|BBB») — из данных.</summary>
    [JsonIgnore] public HashSet<string> Neighbors { get; set; } = new(StringComparer.Ordinal);
    [JsonIgnore] public Dictionary<string, double> History { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Торговля пары по гравитационной модели — без блоков, санкций и договоров.</summary>
    [JsonIgnore] public double[][] Gravity { get; set; } = [];
    /// <summary>
    /// Доступ к рынкам: торговля страны с учётом блоков, санкций и договоров / торговля без них. Start — на 1 января
    /// 2026 года (из данных): экономика уже живёт с тогдашними блоками и санкциями, поправки — разница от старта.
    /// </summary>
    [JsonIgnore] public double[] Access { get; set; } = [];
    [JsonIgnore] public double[] StartAccess { get; set; } = [];
    /// <summary>Санкционное давление: доля мирового ВВП у санкционеров (жёсткие — целиком, частичные — частью). Start — на старте.</summary>
    [JsonIgnore] public double[] Pressure { get; set; } = [];
    [JsonIgnore] public double[] StartPressure { get; set; } = [];
    /// <summary>Отношения от блоков и санкций по парам («AAA|BBB»): (блоки, санкции).</summary>
    [JsonIgnore] public Dictionary<string, (double Blocs, double Sanctions)> Ties { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Индекс мощи (0–100) и его составляющие по странам.</summary>
    [JsonIgnore] public Dictionary<string, PowerScore> Power { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Растёт при каждом пересчёте отношений: снимок для экрана копирует матрицу, только когда она изменилась.</summary>
    [JsonIgnore] public int Version { get; set; }
    [JsonIgnore] public (int Version, VoteTable Table)? VoteCache { get; set; }

    public CountryDiplomacy this[string country] => Countries[country];

    public static string Key(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
}

/// <summary>Договор двух стран: вид (data/diplomacy.json), с какой даты.</summary>
public sealed record Treaty(string A, string B, string Type, DateOnly Since)
{
    public bool Involves(string country) => A == country || B == country;
    public string Other(string country) => A == country ? B : A;
}

/// <summary>Поправки дипломатии для страны: торговля (множитель), производительность, настроение классов, поправки к модели.</summary>
public sealed class CountryDiplomacy
{
    public double TradeFactor { get; set; } = 1;
    public double Productivity { get; set; } = 1;
    public double[] Mood { get; set; } = [];
    public Dictionary<string, double> Effects { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Санкции: кто, против кого, уровень (full — жёсткие, partial — частичные), с какой даты; Response — ответ на чужие санкции.</summary>
public sealed record Sanction(string By, string Target, string Level, DateOnly Since)
{
    public bool Response { get; init; }
}

/// <summary>Выборное место в Совете безопасности — до конца года Until.</summary>
public sealed record CouncilSeat(string Country, int Until);

/// <summary>Резолюция Совета Наций: вид, кто вынес, против кого или в пользу кого, голоса, итог, кто наложил вето.</summary>
public sealed record Resolution(DateOnly Date, string Type, string Proposer, string Target, int Yes, int No, int Abstain, bool Passed, IReadOnlyList<string> Vetoes);

/// <summary>
/// Программа Валютного совета: кредит Amount (доля ВВП) под ставку Rate; к Deadline дефицит должен быть не больше
/// цели; программа идёт до Until. Status: active, done (выполнена), failed (прекращена — до PenaltyUntil занимать дороже).
/// </summary>
public sealed class FundProgram
{
    public DateOnly Since { get; set; }
    public double Amount { get; set; }
    public double Rate { get; set; }
    public DateOnly Deadline { get; set; }
    public DateOnly Until { get; set; }
    public string Status { get; set; } = "active";
    public DateOnly? PenaltyUntil { get; set; }
}

/// <summary>Индекс мощи страны (0–100: у сильнейшей 100) и вклад составляющих в него, место в мире.</summary>
public sealed record PowerScore(double Index, int Rank, double Economy, double Military, double Technology, double Influence);

/// <summary>Строй страны, партия её главы и взгляды этой партии (null — партий нет).</summary>
public sealed record Stance(Regime Regime, string? Party, double[]? Position);

