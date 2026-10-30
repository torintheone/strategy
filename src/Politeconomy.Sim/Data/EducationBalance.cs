namespace Politeconomy.Sim.Data;

// Коэффициенты образования из data/balance.json (раздел education). Смысл — в MODEL.md, «Образование».
// Списки из четырёх чисел — по группам дохода: низкий, ниже среднего, выше среднего, высокий.

public sealed class EducationBalance
{
    /// <summary>Финансирование на ученика = (доля расходов на образование в ВВП / стартовая) / (доля молодёжи 5–24 / стартовая), в пределах FundingMin…FundingMax.</summary>
    public double FundingMin { get; init; } = 0.2;
    public double FundingMax { get; init; } = 3;
    /// <summary>Качество на старте: по группе дохода + QualitySpending × (расходы на образование, % ВВП − SpendingReference).</summary>
    public IReadOnlyList<double> Quality { get; init; } = [35, 50, 65, 78];
    public double QualitySpending { get; init; } = 4;
    public double SpendingReference { get; init; } = 4.5;
    /// <summary>Качество = стартовое × финансирование^QualityElasticity; догоняет цель за QualityMonths.</summary>
    public double QualityElasticity { get; init; } = 0.5;
    public double QualityMonths { get; init; } = 24;
    /// <summary>Охват школой на старте (%), по группе дохода; растёт с финансированием^SchoolElasticity, не выше 100.</summary>
    public IReadOnlyList<double> School { get; init; } = [65, 85, 95, 99];
    public double SchoolElasticity { get; init; } = 0.2;
    /// <summary>Высшее: охват = стартовый × (рост дохода на душу)^TertiaryGrowth × финансирование^TertiaryElasticity × (1 + законы), не выше TertiaryCeiling.</summary>
    public double TertiaryGrowth { get; init; } = 0.5;
    public double TertiaryElasticity { get; init; } = 0.4;
    public double TertiaryCeiling { get; init; } = 100;
    /// <summary>Профессиональное: индекс охвата (1 на старте) = финансирование^VocationalElasticity × (1 + законы).</summary>
    public double VocationalElasticity { get; init; } = 0.3;
    /// <summary>Охват догоняет цель за EnrollMonths (новые места, преподаватели).</summary>
    public double EnrollMonths { get; init; } = 36;
    /// <summary>Через сколько лет охват превращается в выпускников: школа, профессиональное, высшее.</summary>
    public double SchoolLag { get; init; } = 8;
    public double VocationalLag { get; init; } = 3;
    public double TertiaryLag { get; init; } = 5;
    /// <summary>За сколько лет обновляется рабочая сила (выпускники сменяют уходящих на пенсию).</summary>
    public double RenewYears { get; init; } = 40;
    /// <summary>Человеческий капитал = (высшее / старт)^HighWeight × проф.^VocationalWeight × (грамотность / старт)^LiteracyWeight × (качество / старт)^QualityWeight.</summary>
    public double HighWeight { get; init; } = 0.3;
    public double VocationalWeight { get; init; } = 0.15;
    public double LiteracyWeight { get; init; } = 0.2;
    public double QualityWeight { get; init; } = 0.15;
    /// <summary>Рост человеческого капитала на 1% в год прибавляет GrowthElasticity п. п. к росту потенциального ВВП.</summary>
    public double GrowthElasticity { get; init; } = 0.6;
}
