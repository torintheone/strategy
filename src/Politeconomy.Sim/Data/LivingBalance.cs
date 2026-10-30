namespace Politeconomy.Sim.Data;

// Коэффициенты жилья, преступности, коррупции и экологии из data/balance.json (раздел living). Смысл — в MODEL.md,
// «Жильё, преступность, коррупция, экология». Списки из четырёх чисел — по группам дохода.

public sealed class LivingBalance
{
    /// <summary>Жильё: м² на человека на старте; износ в год; желаемая площадь растёт с доходом^DesiredGrowth.</summary>
    public IReadOnlyList<double> Housing { get; init; } = [10, 16, 25, 40];
    public double Depreciation { get; init; } = 0.015;
    public double DesiredGrowth { get; init; } = 0.5;
    /// <summary>Стройка отвечает на цены: ввод × (желаемая / имеющаяся)^BuildResponse.</summary>
    public double BuildResponse { get; init; } = 3;
    /// <summary>Цены на жильё к доходам: индекс 100 на старте × (желаемая / имеющаяся)^PriceElasticity × (1 + поправки).</summary>
    public double PriceElasticity { get; init; } = 2;
    /// <summary>Нехватка жилья (доля от желаемой): удовлетворённость − HousingMood × нехватка, рождаемость × (1 − HousingFertility × нехватка).</summary>
    public double HousingMood { get; init; } = 30;
    public double HousingFertility { get; init; } = 0.5;
    /// <summary>Преступность (0–100) на старте по группам дохода; цель = стартовая + вклады, × (1 + законы и программы); догоняет за CrimeMonths.</summary>
    public IReadOnlyList<double> Crime { get; init; } = [55, 45, 35, 22];
    public double CrimeUnemployment { get; init; } = 1.5;
    public double CrimeLiving { get; init; } = 0.5;
    public double CrimeYouth { get; init; } = 100;
    public double CrimePolice { get; init; } = 15;
    public double CrimeCorruption { get; init; } = 0.3;
    public double CrimeMonths { get; init; } = 12;
    /// <summary>Деньги на полицию и зарплаты чиновников (к стартовым) действуют в пределах от 1/SpendingRange до SpendingRange раз.</summary>
    public double SpendingRange { get; init; } = 2;
    /// <summary>Рост преступности на пункт: удовлетворённость −CrimeMood, легитимность −CrimeLegitimacy, рост −CrimeGrowth п. п.</summary>
    public double CrimeMood { get; init; } = 0.1;
    public double CrimeLegitimacy { get; init; } = 0.1;
    public double CrimeGrowth { get; init; } = 0.005;
    /// <summary>Коррупция: зарплаты чиновников (расходы на управление к старту) снижают индекс на CorruptionWages × ln(отношение).</summary>
    public double CorruptionWages { get; init; } = 8;
    /// <summary>Рост коррупции на пункт: налоги собираются хуже на CorruptionTaxes (доля), рост −CorruptionGrowth п. п., легитимность −CorruptionLegitimacy.</summary>
    public double CorruptionTaxes { get; init; } = 0.004;
    public double CorruptionGrowth { get; init; } = 0.01;
    public double CorruptionLegitimacy { get; init; } = 0.1;
    /// <summary>Загрязнение (0–100) на старте: по группам дохода + PollutionIndustry × (промышленность, % ВВП − 30).</summary>
    public IReadOnlyList<double> Pollution { get; init; } = [40, 55, 50, 30];
    public double PollutionIndustry { get; init; } = 0.8;
    /// <summary>
    /// Цель = стартовое × (промышленный выпуск / стартовый)^PollutionOutput × (доход на душу / стартовый)^−PollutionIncome
    /// × e^(−PollutionTech × лет) × (1 + законы); догоняет за PollutionMonths. Богатеющие люди требуют чистого воздуха.
    /// </summary>
    public double PollutionOutput { get; init; } = 0.5;
    public double PollutionIncome { get; init; } = 0.2;
    public double PollutionTech { get; init; } = 0.015;
    public double PollutionMonths { get; init; } = 12;
    /// <summary>Рост загрязнения на пункт: продолжительность жизни −PollutionLife лет; удовлетворённость горожан (специалисты, служащие) −PollutionUrban, остальных −PollutionMood.</summary>
    public double PollutionLife { get; init; } = 0.06;
    public double PollutionUrban { get; init; } = 0.15;
    public double PollutionMood { get; init; } = 0.05;
}
