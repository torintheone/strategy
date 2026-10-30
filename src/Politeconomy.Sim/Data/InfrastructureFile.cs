namespace Politeconomy.Sim.Data;

// Инфраструктура и стройки. Поля описаны в data/README.md, смысл — в MODEL.md, «Инфраструктура и стройки».

public sealed record InfrastructureFile(
    IReadOnlyList<InfrastructureRecord> Types,
    IReadOnlyList<ProjectRecord> Projects,
    IReadOnlyList<string> Lenders);

/// <summary>
/// Вид инфраструктуры. Post — какое ведомство за него отвечает; Budget — из какой статьи расходов его содержат и
/// строят (null — частные деньги); Stock — сколько он стоит (% ВВП); Wear — износ в год без обслуживания;
/// BuildYears — срок стройки; Households — доля населения в нагрузке; Default и Weights — кто его нагружает
/// (вес отрасли, по умолчанию Default); Driver — особая нагрузка: trade (внешняя торговля), power (спрос на
/// электричество); Coast — только у стран с выходом к морю.
/// </summary>
public sealed record InfrastructureRecord(
    string Id,
    string Name,
    string Post,
    string? Budget,
    double Stock,
    double Wear,
    double BuildYears,
    double Households,
    double Default,
    IReadOnlyDictionary<string, double> Weights,
    string? Driver = null,
    bool Coast = false);

/// <summary>
/// Стройка, которую может начать игрок. Type — вид инфраструктуры или Plant — тип электростанции; Kind — capacity
/// (новая мощность) или repair (ремонт: состояние); Gain — прибавка: доля мощности, состояния или — у станций — доля
/// нужной выработки; Months — срок (у станций — срок стройки типа); MinIncome — с какой группы дохода доступна.
/// </summary>
public sealed record ProjectRecord(
    string Id,
    string Name,
    string Description,
    double Gain,
    string? Type = null,
    string? Plant = null,
    string Kind = "capacity",
    double Months = 0,
    int MinIncome = 0);

// Коэффициенты инфраструктуры из data/balance.json (раздел infrastructure). Списки из четырёх чисел — по группам дохода.

public sealed class InfrastructureBalance
{
    /// <summary>Запас мощности на старте и цель стройки.</summary>
    public IReadOnlyList<double> Reserve { get; init; } = [0.05, 0.08, 0.12, 0.15];
    /// <summary>Состояние на старте (доля от нового).</summary>
    public IReadOnlyList<double> Condition { get; init; } = [0.6, 0.7, 0.8, 0.85];
    public double ConditionMin { get; init; } = 0.3;
    /// <summary>Нагрузка населения растёт с доходом на душу в степени HouseholdIncome.</summary>
    public double HouseholdIncome { get; init; } = 0.5;
    /// <summary>Стройка: как в энергетике (недостаток мощности — за ReserveMonths, стройки — за PipelineMonths, в год — не больше BuildMax × деньги).</summary>
    public double ReserveMonths { get; init; } = 24;
    public double PipelineMonths { get; init; } = 24;
    public double BuildMax { get; init; } = 0.1;
    public double GrowthMonths { get; init; } = 24;
    public double GrowthMax { get; init; } = 0.1;
    /// <summary>
    /// Деньги на обслуживание: (доля статьи в бюджете / стартовая, в пределах 1/SpendingRange…SpendingRange)^FundingSpending ×
    /// (весь бюджет к ВВП / стартовый)^FundingTotal; на стройку — ещё × (инвестиции / стартовые)^FundingInvestment × (1 − FundingCorruption × рост коррупции).
    /// </summary>
    public double FundingSpending { get; init; } = 1;
    public double FundingTotal { get; init; } = 0.3;
    public double FundingInvestment { get; init; } = 0.5;
    public double FundingCorruption { get; init; } = 0.01;
    public double SpendingRange { get; init; } = 2;
    public double FundingMin { get; init; } = 0.1;
    public double FundingMax { get; init; } = 3;
    /// <summary>Перегрузка режет выпуск отрасли: InfraCut × (1 − обеспеченность) × вес отрасли / средний, всего не больше CutMax.</summary>
    public double InfraCut { get; init; } = 0.5;
    public double CutMax { get; init; } = 0.3;
    /// <summary>Нехватка водопровода повышает потери от болезней: + WaterHealth × (1 − обеспеченность).</summary>
    public double WaterHealth { get; init; } = 0.5;
    /// <summary>Коррупция в стройках: цена × (1 + ProjectCorruption × (коррупция − CorruptionFree) / 100), срок × (1 + ProjectDelay × …).</summary>
    public double ProjectCorruption { get; init; } = 1;
    public double ProjectDelay { get; init; } = 0.5;
    public double CorruptionFree { get; init; } = 20;
    /// <summary>Ремонт стоит RepairCost от цены новой мощности того же объёма.</summary>
    public double RepairCost { get; init; } = 0.4;
    /// <summary>Концессия: только при коррупции ниже PppMaxCorruption; инвестор берёт PppMargin сверх цены; люди платят за пользование ConcessionYears лет — удовлетворённость −PppMood за процент ВВП цены.</summary>
    public double PppMaxCorruption { get; init; } = 60;
    public double PppMargin { get; init; } = 0.3;
    public double ConcessionYears { get; init; } = 15;
    public double PppMood { get; init; } = 0.5;
}
