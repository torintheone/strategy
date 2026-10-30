namespace Politeconomy.Sim.Education;

/// <summary>
/// Образование страны. Для каждого уровня — охват сейчас, выпуск (охват прошлых лет, дошедший до рынка труда) и запас
/// в рабочей силе: школа (охват и грамотность, %), профессиональное (индекс, 1 на старте), высшее (охват, %; запас —
/// в тех же единицах). Качество 0–100. Человеческий капитал — индекс, 1 на старте; Effects — его вклад в рост.
/// MODEL.md, «Образование».
/// </summary>
public sealed class CountryEducation
{
    public double School { get; set; }
    public double SchoolGraduates { get; set; }
    public double Literacy { get; set; }
    public double Vocational { get; set; } = 1;
    public double VocationalGraduates { get; set; } = 1;
    public double VocationalStock { get; set; } = 1;
    public double Tertiary { get; set; }
    public double TertiaryGraduates { get; set; }
    public double TertiaryStock { get; set; }
    public double Quality { get; set; }
    /// <summary>Финансирование на ученика относительно старта (1 — как на старте).</summary>
    public double Funding { get; set; } = 1;
    public double HumanCapital { get; set; } = 1;

    // Стартовые значения — от них считаются относительные изменения.
    public double Spending0 { get; set; }
    public double Youth0 { get; set; }
    public double Tertiary0 { get; set; }
    public double Literacy0 { get; set; }
    public double Quality0 { get; set; }

    /// <summary>Поправки к модели (сейчас — к росту потенциального ВВП от человеческого капитала).</summary>
    public Dictionary<string, double> Effects { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorldEducation
{
    public Dictionary<string, CountryEducation> Countries { get; set; } = new(StringComparer.Ordinal);

    public CountryEducation this[string country] => Countries[country];
}
