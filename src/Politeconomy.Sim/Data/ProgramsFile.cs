namespace Politeconomy.Sim.Data;

// Программы ведомств. Поля описаны в data/README.md.

public sealed record ProgramsFile(IReadOnlyList<ProgramRecord> Programs);

/// <summary>
/// Программа ведомства: включается сразу, без парламента. Cost — сколько стоит (% ВВП в год, идёт в расходы на статью
/// ведомства); Effects — поправки к модели (как у законов); Start — работает ли программа на старте, по группам дохода;
/// MinDoctors — без скольких врачей на 1000 жителей она почти бесполезна (0 — без условия).
/// </summary>
public sealed record ProgramRecord(
    string Id,
    string Post,
    string Name,
    string Description,
    double Cost,
    IReadOnlyDictionary<string, double> Effects,
    IReadOnlyList<bool> Start,
    double MinDoctors = 0);

// Коэффициенты здравоохранения и социальной защиты из data/balance.json (раздел health). Смысл — в MODEL.md,
// «Здравоохранение». Списки из четырёх чисел — по группам дохода.

public sealed class HealthBalance
{
    /// <summary>Потребность в медицине на человека: 1 + ElderlyNeed × доля людей 65+.</summary>
    public double ElderlyNeed { get; init; } = 2;
    /// <summary>Финансирование = (доля расходов на здравоохранение в ВВП / стартовая) / (потребность / стартовая), в пределах FundingMin…FundingMax.</summary>
    public double FundingMin { get; init; } = 0.2;
    public double FundingMax { get; init; } = 3;
    /// <summary>Врачи и койки на 1000 жителей на старте; цель = стартовое × финансирование^эластичность; догоняют за годы (учёба, стройка).</summary>
    public IReadOnlyList<double> Doctors { get; init; } = [0.3, 0.9, 2.3, 3.4];
    public IReadOnlyList<double> Beds { get; init; } = [0.9, 1.5, 3.0, 4.5];
    public double DoctorsElasticity { get; init; } = 0.7;
    public double BedsElasticity { get; init; } = 0.5;
    public double DoctorsYears { get; init; } = 6;
    public double BedsYears { get; init; } = 3;
    /// <summary>Доступность медицины (% людей) по системе: частная, страховая, государственная — по группам дохода; догоняет за AccessMonths.</summary>
    public IReadOnlyList<double> AccessPrivate { get; init; } = [35, 50, 70, 80];
    public IReadOnlyList<double> AccessInsurance { get; init; } = [50, 70, 85, 95];
    public IReadOnlyList<double> AccessState { get; init; } = [60, 80, 92, 99];
    public double AccessMonths { get; init; } = 24;
    /// <summary>Мощность = (врачи / старт)^DoctorsWeight × (койки / старт)^BedsWeight; индекс здоровья = мощность^(1 − AccessWeight) × (доступность / старт)^AccessWeight.</summary>
    public double DoctorsWeight { get; init; } = 0.5;
    public double BedsWeight { get; init; } = 0.2;
    public double AccessWeight { get; init; } = 0.4;
    /// <summary>Продолжительность жизни: + LifeScale × ln(индекс здоровья) лет.</summary>
    public double LifeScale { get; init; } = 6;
    /// <summary>Потери рабочего времени из-за болезней на старте (%) по группам дохода; = стартовые / индекс^BurdenElasticity × (1 + программы).</summary>
    public IReadOnlyList<double> Burden { get; init; } = [9, 6, 4, 3];
    public double BurdenElasticity { get; init; } = 0.7;
    /// <summary>Пенсии: расходы на социальную защиту растут вместе с долей людей 65+ (эластичность PensionAging).</summary>
    public double PensionAging { get; init; } = 1;
}
