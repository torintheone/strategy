namespace Politeconomy.Sim.Data;

// Группы интересов и религиозность стран. Поля описаны в data/README.md, смысл — в MODEL.md, «Группы интересов».

public sealed record InterestGroupsFile(IReadOnlyList<InterestGroupRecord> Groups, ReligiosityRecord Religiosity);

/// <summary>
/// Группа интересов. Classes — на какие классы она опирается (доля класса, которую она представляет);
/// Industries — если заданы, группе достаётся часть влияния крупного капитала по доле этих отраслей в выпуске.
/// Clout — множитель влияния, Regime — по режимам (демократия, гибридный, авторитарный), Military — при военном
/// режиме, Monarchy — при абсолютной монархии; Religious — влияние × квадрат религиозности страны. Lean — сдвиг взглядов
/// группы от взглядов её классов. Spending — статьи бюджета, за которыми она следит; Deficit — как сильно её злит
/// дефицит сверх 3% ВВП. Laws — собственное отношение к вариантам законов (сверх отношения её классов).
/// Effects — поправки к модели при полном недовольстве (у довольной — обратные, слабее). Pressure и Support —
/// что группа делает, когда недовольна и когда довольна.
/// </summary>
public sealed record InterestGroupRecord(
    string Id,
    string Name,
    string Description,
    string Color,
    IReadOnlyList<double> Lean,
    IReadOnlyDictionary<string, double> Classes,
    double Clout,
    IReadOnlyList<double> Regime,
    IReadOnlyList<string> Spending,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Laws,
    IReadOnlyDictionary<string, double> Effects,
    string Pressure,
    string Support,
    IReadOnlyList<string>? Industries = null,
    double Military = 1,
    double Monarchy = 1,
    bool Religious = false,
    double Deficit = 0)
{
    /// <summary>Собственное отношение группы к варианту закона (0, если не задано).</summary>
    public double LawAttitude(string law, string variant) => Laws.GetValueOrDefault(law)?.GetValueOrDefault(variant) ?? 0;
}

/// <summary>Религиозность 0–1: по умолчанию, по субрегионам и по отдельным странам.</summary>
public sealed record ReligiosityRecord(double Default, IReadOnlyDictionary<string, double> Subregions, IReadOnlyDictionary<string, double> Countries);

// Коэффициенты групп интересов из data/balance.json (раздел interest_groups). Смысл — в MODEL.md, «Группы интересов».

public sealed class InterestGroupsBalance
{
    /// <summary>
    /// Во сколько раз влияние крупного капитала в группах интересов больше его влияния как класса: деньги, связи,
    /// собственные СМИ. Делится между группами с отраслями.
    /// </summary>
    public double CapitalClout { get; init; } = 15;
    /// <summary>Вес капиталистов среди классов группы со своими классами (аграрии): для настроения и отношения к законам.</summary>
    public double CapitalWeight { get; init; } = 0.3;
    /// <summary>Одобрение: за единицу удовлетворённости классов выше 50, за единицу собственного отношения к законам (с начала игры), за рост расходов (логарифм), за близость правительства.</summary>
    public double ClassWeight { get; init; } = 0.6;
    public double LawWeight { get; init; } = 3;
    /// <summary>Предел вклада законов в одобрение (в обе стороны).</summary>
    public double LawCap { get; init; } = 30;
    public double BudgetWeight { get; init; } = 30;
    public double BudgetCap { get; init; } = 0.7;
    public double AlignWeight { get; init; } = 40;
    /// <summary>Дефицит, который финансисты терпят (% ВВП), и предел их недовольства дефицитом.</summary>
    public double DeficitTolerance { get; init; } = 3;
    public double DeficitCap { get; init; } = 15;
    /// <summary>За сколько месяцев одобрение догоняет новые условия.</summary>
    public double ApprovalMonths { get; init; } = 4;
    /// <summary>Выполненное требование: прибавка к одобрению и как она тает за месяц.</summary>
    public double DemandBonus { get; init; } = 8;
    public double DemandMemory { get; init; } = 0.97;
    /// <summary>Требование закона — если отношение группы к варианту лучше нынешнего хотя бы на столько; сколько законов требовать; на сколько поднять расходы.</summary>
    public double DemandThreshold { get; init; } = 1.5;
    public int LawDemands { get; init; } = 2;
    public double SpendingDemand { get; init; } = 0.1;
    /// <summary>Пороги: ниже Angry группа давит на власть (в полную силу — при 0), выше Calm — помогает (слабее, × HappyShare).</summary>
    public double Angry { get; init; } = 40;
    public double Calm { get; init; } = 60;
    public double HappyShare { get; init; } = 0.4;
    /// <summary>Сила давления: доля влияния × число групп, не больше MaxWeight (средняя группа — 1).</summary>
    public double MaxWeight { get; init; } = 2;
    /// <summary>Деньги на выборы: довольная группа (выше FundHappy) — правящим, недовольная (ниже FundAngry) — ближайшей оппозиции.</summary>
    public double FundWeight { get; init; } = 0.6;
    public double FundHappy { get; init; } = 55;
    public double FundAngry { get; init; } = 45;
    /// <summary>Лоббирование: сдвиг одобрения законопроекта = LobbyWeight × Σ влияние × th(изменение отношения группы / LobbyScale).</summary>
    public double LobbyWeight { get; init; } = 0.2;
    public double LobbyScale { get; init; } = 2;
}
