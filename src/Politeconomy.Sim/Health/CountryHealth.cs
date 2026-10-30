namespace Politeconomy.Sim.Health;

/// <summary>
/// Здравоохранение страны: врачи и койки на 1000 жителей, доступность медицины (% людей), индекс здоровья (1 на
/// старте), потери рабочего времени из-за болезней (%). Effects — поправки к модели: продолжительность жизни, рост,
/// расходы на пенсии из-за старения. MODEL.md, «Здравоохранение».
/// </summary>
public sealed class CountryHealth
{
    public double Doctors { get; set; }
    public double Beds { get; set; }
    public double Access { get; set; }
    public double Funding { get; set; } = 1;
    public double Index { get; set; } = 1;
    public double Burden { get; set; }

    public double Doctors0 { get; set; }
    public double Beds0 { get; set; }
    public double Access0 { get; set; }
    public double Burden0 { get; set; }
    public double Spending0 { get; set; }
    public double Need0 { get; set; }
    public double Elderly0 { get; set; }

    public Dictionary<string, double> Effects { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorldHealth
{
    public Dictionary<string, CountryHealth> Countries { get; set; } = new(StringComparer.Ordinal);

    public CountryHealth this[string country] => Countries[country];
}
