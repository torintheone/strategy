namespace Politeconomy.Sim.Science;

/// <summary>
/// Наука страны: освоенные технологии (и какие были на старте), распределение очков по ветвям (доли, порядок
/// data/technologies.json), накопленные очки по технологиям (млрд долларов), приоритетная технология, очки в год
/// (% ВВП, с эффективностью), эффективность, утечка мозгов, сглаженные расходы на исследования к старту. Productivity — множитель производительности
/// секторов, Effects — поправки к модели. MODEL.md, «Наука».
/// </summary>
public sealed class CountryScience
{
    public HashSet<string> Researched { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> Start { get; set; } = new(StringComparer.Ordinal);
    public double[] Allocation { get; set; } = [];
    /// <summary>Отсортирован: из него удаляют освоенное, и порядок записей в сохранении не должен зависеть от истории.</summary>
    public SortedDictionary<string, double> Progress { get; set; } = new(StringComparer.Ordinal);
    public string? Priority { get; set; }
    public double Points { get; set; }
    /// <summary>Расходы на исследования (% ВВП, без эффективности) на старте.</summary>
    public double Spending0 { get; set; }
    public double Effort { get; set; } = 1;
    public double Efficiency { get; set; } = 1;
    public double Drain { get; set; }
    public double Drain0 { get; set; }
    public double Specialists0 { get; set; }
    public double Quality0 { get; set; }
    public double Freedom0 { get; set; }
    public double Investment0 { get; set; }
    /// <summary>Относительный вклад технологий на старте (по ключам с поправкой на распространение в мире).</summary>
    public Dictionary<string, double> Base { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Productivity { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Effects { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Наука мира: страны и доля мира (по ВВП по ППС), которая уже освоила каждую технологию.</summary>
public sealed class WorldScience
{
    public Dictionary<string, CountryScience> Countries { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Adoption { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Средний по миру (по ВВП) выигрыш от технологий к старту — по относительным ключам.</summary>
    public Dictionary<string, double> Mean { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Кто первым освоил технологию, которую на старте не знал никто, и когда.</summary>
    public Dictionary<string, Discovery> Discoveries { get; set; } = new(StringComparer.Ordinal);

    public CountryScience this[string country] => Countries[country];
}

public sealed record Discovery(string Country, DateOnly Date);
