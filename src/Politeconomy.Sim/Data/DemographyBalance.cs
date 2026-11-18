namespace Politeconomy.Sim.Data;

// Коэффициенты демографии из data/balance.json (раздел demography). Смысл каждого — в MODEL.md, «Демография».

public sealed class DemographyBalance
{
    /// <summary>Мальчиков на одну девочку при рождении; отдельные страны — в <see cref="SexRatioOverrides"/>.</summary>
    public double SexRatioAtBirth { get; init; } = 1.05;
    public IReadOnlyDictionary<string, double> SexRatioOverrides { get; init; } = new Dictionary<string, double>();

    /// <summary>Доли рождений по возрасту матери 15–19 … 45–49 (сумма 1); 15–19 уточняется по данным о подростковой рождаемости.</summary>
    public IReadOnlyList<double> FertilitySchedule { get; init; } = [0.06, 0.2, 0.28, 0.24, 0.15, 0.06, 0.01];

    /// <summary>Насколько падает рождаемость на каждый процентный пункт безработицы выше естественной.</summary>
    public double FertilityUnemployment { get; init; } = 0.015;
    /// <summary>Рождаемость без прогноза: стремится к этому уровню со скоростью FertilitySpeed в год.</summary>
    public double FertilityFloor { get; init; } = 1.6;
    public double FertilitySpeed { get; init; } = 0.03;

    /// <summary>Рост продолжительности жизни без прогноза: Min + Extra × доля пути от 85 лет вниз до 60.</summary>
    public double LifeGainMin { get; init; } = 0.1;
    public double LifeGainExtra { get; init; } = 0.25;

    /// <summary>Модель смертности Силера: μ(x) = a1·e^(−b1·x) + L·(a2 + a3·e^(b3·x)).</summary>
    public double ChildDecline { get; init; } = 1.0;
    public double Background { get; init; } = 0.0003;
    public double Senescence { get; init; } = 0.000037;
    public double SenescenceSlope { get; init; } = 0.09;
    /// <summary>У мальчиков младенческая смертность выше, чем у девочек, во столько раз.</summary>
    public double InfantMaleExcess { get; init; } = 1.15;

    /// <summary>Чистая миграция со временем затухает: вдвое за столько лет.</summary>
    public double MigrationHalfLifeYears { get; init; } = 30;

    /// <summary>Доли мигрантов по возрасту 0–4 … 80+ (сумма 1), одинаковые для обоих полов.</summary>
    public IReadOnlyList<double> MigrationProfile { get; init; } =
        [0.06, 0.05, 0.04, 0.07, 0.14, 0.16, 0.14, 0.1, 0.08, 0.05, 0.04, 0.03, 0.02, 0.01, 0.005, 0.003, 0.002];

    /// <summary>Пожилые в рабочей силе: такая доля от участия людей 15–64 лет.</summary>
    public double OlderParticipation { get; init; } = 0.1;

    /// <summary>Урбанизация замедляется по мере приближения к UrbanMax.</summary>
    public double UrbanMax { get; init; } = 98;
}
