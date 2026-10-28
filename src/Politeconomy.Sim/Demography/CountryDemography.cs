using System.Text.Json.Serialization;

namespace Politeconomy.Sim.Demography;

/// <summary>
/// Население страны по пятилетним группам (0–4 … 75–79, 80+) и полу, коэффициенты рождаемости и смертности,
/// миграция и урбанизация. Годовые коэффициенты в помесячном шаге делятся на 12.
/// </summary>
public sealed class CountryDemography
{
    public const int Groups = 17;
    /// <summary>Группы матерей 15–19 … 45–49.</summary>
    public const int FirstMotherGroup = 3;
    public const int MotherGroups = 7;

    public double[] Female { get; set; } = new double[Groups];
    public double[] Male { get; set; } = new double[Groups];
    /// <summary>Годовые коэффициенты смертности по группам (умерших на человека в год).</summary>
    public double[] MortalityFemale { get; set; } = new double[Groups];
    public double[] MortalityMale { get; set; } = new double[Groups];
    /// <summary>Доли рождений по возрасту матери 15–19 … 45–49.</summary>
    public double[] FertilityShares { get; set; } = new double[MotherGroups];

    /// <summary>Суммарная рождаемость: обычный уровень страны и текущий с поправкой на экономику.</summary>
    public double BaseFertility { get; set; }
    public double Fertility { get; set; }
    public double FertilityGap0 { get; set; }
    public double LifeExpectancy { get; set; }
    public double LifeGap { get; set; }
    public double LifeGap0 { get; set; }
    public double InfantMortality { get; set; }
    public double InfantMortality0 { get; set; }
    public double LifeExpectancy0 { get; set; }
    public double SexRatioAtBirth { get; set; } = 1.05;
    /// <summary>Чистая миграция: человек в год на одного жителя.</summary>
    public double NetMigrationRate { get; set; }
    /// <summary>Участие в рабочей силе: рабочая сила / (люди 15–64 + доля пожилых).</summary>
    public double Participation { get; set; }
    public double UrbanPct { get; set; }
    public double UrbanPct0 { get; set; }
    /// <summary>Рост доли горожан на старте, процентных пунктов в год.</summary>
    public double UrbanChange { get; set; }
    /// <summary>Рост рабочей силы, годовой темп по последнему месяцу.</summary>
    public double LaborGrowth { get; set; }

    // История за 12 месяцев: значение год назад лежит в ячейке HistoryIndex.
    public double[] BirthsHistory { get; set; } = new double[12];
    public double[] DeathsHistory { get; set; } = new double[12];
    public double[] MigrationHistory { get; set; } = new double[12];
    public double[] TotalHistory { get; set; } = new double[12];
    public int HistoryIndex { get; set; }

    [JsonIgnore]
    public double Total => Female.Sum() + Male.Sum();
    [JsonIgnore]
    public double LifeExpectancyFemale => LifeExpectancy + LifeGap / 2;
    [JsonIgnore]
    public double LifeExpectancyMale => LifeExpectancy - LifeGap / 2;

    /// <summary>Люди в группах с <paramref name="from"/> по <paramref name="to"/> включительно, оба пола.</summary>
    public double Sum(int from, int to)
    {
        var sum = 0.0;
        for (var g = from; g <= to; g++)
            sum += Female[g] + Male[g];
        return sum;
    }

    public double WorkingAge => Sum(3, 12);
    public double Elderly => Sum(13, Groups - 1);
    public double Children => Sum(0, 2);

    public double LaborForce(double olderParticipation) => Participation * (WorkingAge + olderParticipation * Elderly);

    /// <summary>Медианный возраст: внутри пятилетней группы люди распределены равномерно.</summary>
    public double MedianAge()
    {
        var half = Total / 2;
        var acc = 0.0;
        for (var g = 0; g < Groups; g++)
        {
            var n = Female[g] + Male[g];
            if (acc + n >= half && n > 0)
                return g * 5 + (g == Groups - 1 ? 10 : 5) * (half - acc) / n;
            acc += n;
        }
        return 0;
    }

    public double YearSum(double[] history) => history.Sum();

    public void PushHistory(double births, double deaths, double migration)
    {
        BirthsHistory[HistoryIndex] = births;
        DeathsHistory[HistoryIndex] = deaths;
        MigrationHistory[HistoryIndex] = migration;
        TotalHistory[HistoryIndex] = Total;
        HistoryIndex = (HistoryIndex + 1) % 12;
    }
}

/// <summary>Демография всех стран.</summary>
public sealed class WorldDemography
{
    public Dictionary<string, CountryDemography> Countries { get; set; } = new(StringComparer.Ordinal);

    public CountryDemography this[string country] => Countries[country];
}
