using System.Text.Json.Serialization;

namespace Politeconomy.Sim.Society;

/// <summary>Порядок классов — как в data/classes.json (проверяет validate).</summary>
public static class ClassIds
{
    public const int Unemployed = 0, Unskilled = 1, Skilled = 2, Farmers = 3, Clerks = 4, Professionals = 5,
        SmallBusiness = 6, Capitalists = 7, Officials = 8, Pensioners = 9;
    public const int Count = 10;

    public static readonly string[] Ids =
        ["unemployed", "unskilled", "skilled", "farmers", "clerks", "professionals", "small_business", "capitalists", "officials", "pensioners"];
}

/// <summary>
/// Классы одной страны, массивы — по порядку <see cref="ClassIds"/>. Численность — люди вместе с детьми
/// и неработающими членами семей. Доходы, потребление и сбережения — на человека, в долларах по паритету
/// покупательной способности в ценах 2026 года (в год). Уровень жизни, ожидания, удовлетворённость
/// и радикализм — от 0 до 100; влияние — доля в процентах, сумма по классам 100.
/// </summary>
public sealed class CountrySociety
{
    /// <summary>Работающие, безработные и пенсионеры — без детей и неработающих членов семей.</summary>
    public double[] Heads { get; set; } = new double[ClassIds.Count];
    public double[] Size { get; set; } = new double[ClassIds.Count];
    public double[] Income { get; set; } = new double[ClassIds.Count];
    public double[] Consumption { get; set; } = new double[ClassIds.Count];
    public double[] Savings { get; set; } = new double[ClassIds.Count];
    public double[] StandardOfLiving { get; set; } = new double[ClassIds.Count];
    public double[] Expectations { get; set; } = new double[ClassIds.Count];
    public double[] Satisfaction { get; set; } = new double[ClassIds.Count];
    public double[] Radicalism { get; set; } = new double[ClassIds.Count];
    public double[] Organization { get; set; } = new double[ClassIds.Count];
    public double[] Influence { get; set; } = new double[ClassIds.Count];

    /// <summary>Поправка уровня доходов к данным о потреблении домохозяйств; задаётся на старте.</summary>
    public double Scale { get; set; } = 1;
    /// <summary>Сколько людей за последний месяц перешло в другие классы.</summary>
    public double Mobility { get; set; }

    // Из чего сложилась удовлетворённость в последний месяц (средние по населению) — для подсказки.
    public double MoodChange { get; set; }
    public double MoodLevel { get; set; }
    public double MoodFear { get; set; }
    public double MoodInflation { get; set; }
    public double MoodLaws { get; set; }

    [JsonIgnore]
    public double Total => Size.Sum();

    /// <summary>Среднее по населению: каждый класс весит своей численностью.</summary>
    public double Average(double[] values)
    {
        var total = Total;
        if (total <= 0)
            return 0;
        var sum = 0.0;
        for (var k = 0; k < ClassIds.Count; k++)
            sum += values[k] * Size[k];
        return sum / total;
    }
}

public sealed class WorldSociety
{
    public Dictionary<string, CountrySociety> Countries { get; set; } = new(StringComparer.Ordinal);

    public CountrySociety this[string country] => Countries[country];
}
