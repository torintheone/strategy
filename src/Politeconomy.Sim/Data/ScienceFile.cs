namespace Politeconomy.Sim.Data;

// Наука и технологии. Поля описаны в data/README.md, смысл — в MODEL.md, «Наука».

public sealed record ScienceFile(IReadOnlyList<BranchRecord> Branches, IReadOnlyList<TechnologyRecord> Technologies);

public sealed record BranchRecord(string Id, string Name);

/// <summary>
/// Технология: ветвь, уровень (1 — самые старые; на старте страна знает уровни по своему развитию, последние два не
/// знает никто), эффекты (ключи — как у законов; productivity.&lt;сектор&gt; — производительность сектора) и что нужно
/// освоить раньше (кроме предыдущего уровня своей ветви).
/// </summary>
public sealed record TechnologyRecord(
    string Id,
    string Branch,
    int Tier,
    string Name,
    string Description,
    IReadOnlyDictionary<string, double> Effects,
    IReadOnlyList<string>? Requires = null);

// Коэффициенты науки из data/balance.json (раздел science). Списки из четырёх чисел — по группам дохода.

public sealed class ScienceBalance
{
    /// <summary>Индекс развития страны (0–100) = 100 × ln(ВВП на душу по ППС / IndexFloor) / ln(IndexCeiling / IndexFloor); на старте освоены уровни, чей порог Start не выше индекса.</summary>
    public double IndexFloor { get; init; } = 1000;
    public double IndexCeiling { get; init; } = 100000;
    public IReadOnlyList<double> Start { get; init; } = [10, 30, 50, 65, 82, 999, 999];
    /// <summary>
    /// Цена технологии (млрд долларов исследований): первооткрыватель платит FrontierCost × TierCost[уровень]; когда технологию
    /// уже кто-то освоил — BaseCost % ВВП страны по ППС × TierCost × (1 − Diffusion × доля мира, которая её освоила), но не больше цены открытия.
    /// </summary>
    public double FrontierCost { get; init; } = 600;
    public double BaseCost { get; init; } = 0.8;
    public IReadOnlyList<double> TierCost { get; init; } = [0.5, 0.8, 1.2, 1.8, 2.5, 4, 6];
    public double Diffusion { get; init; } = 0.85;
    /// <summary>Частные исследования (% ВВП) по группам дохода.</summary>
    public IReadOnlyList<double> Private { get; init; } = [0.1, 0.2, 0.6, 1.6];
    /// <summary>Эффективность (1 на старте) = (доля специалистов)^EfficiencySpecialists × (качество образования)^EfficiencyQuality × (свободы)^EfficiencyFreedom (всё — к старту) × (1 − рост утечки мозгов).</summary>
    public double EfficiencySpecialists { get; init; } = 0.5;
    public double EfficiencyQuality { get; init; } = 0.5;
    public double EfficiencyFreedom { get; init; } = 0.3;
    /// <summary>Утечка мозгов (доля в год) = DrainMood × недовольство специалистов (50 − удовлетворённость, доля) + DrainFreedom × несвобода (60 − гражданские свободы, доля).</summary>
    public double DrainMood { get; init; } = 0.06;
    public double DrainFreedom { get; init; } = 0.04;
    /// <summary>Приоритетная технология получает PriorityShare всех очков.</summary>
    public double PriorityShare { get; init; } = 0.5;
    /// <summary>Рост: + ScienceGrowth п. п. × ln(расходы на исследования к стартовым, % ВВП), сглаженные за EffortYears лет.</summary>
    public double ScienceGrowth { get; init; } = 0.4;
    public double EffortYears { get; init; } = 5;
}
