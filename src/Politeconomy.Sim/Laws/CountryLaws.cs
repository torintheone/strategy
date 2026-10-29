using Politeconomy.Sim.Government;

namespace Politeconomy.Sim.Laws;

/// <summary>
/// Законы страны: какой вариант каждого закона действует сейчас и какой действовал на старте, законопроекты
/// в работе, переходные периоды и итоговые поправки к модели (Modifiers) и к настроению классов (Attitudes).
/// </summary>
public sealed class CountryLaws
{
    public Dictionary<string, string> Current { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Start { get; set; } = new(StringComparer.Ordinal);
    public List<Bill> Bills { get; set; } = [];
    /// <summary>Последние рассмотренные законопроекты (принятые и отклонённые), новые — в конце.</summary>
    public List<Bill> Finished { get; set; } = [];
    public List<LawTransition> Transitions { get; set; } = [];
    /// <summary>Сумма эффектов всех законов относительно старта, по ключам из laws.json → effects.</summary>
    public Dictionary<string, double> Modifiers { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Сдвиг отношения каждого класса к законам относительно старта (порядок classes.json).</summary>
    public double[] Attitudes { get; set; } = [];
    /// <summary>Средняя коррупционность министров на старте — от неё считается вклад министров в индекс коррупции.</summary>
    public double StartCorruption { get; set; }
    /// <summary>Растёт при каждом изменении — по нему интерфейс понимает, что пора перерисовать.</summary>
    public int Version { get; set; }

    public Bill? BillFor(string law) => Bills.FirstOrDefault(b => b.Law == law);

    public double Effect(string key) => Modifiers.GetValueOrDefault(key);
}

/// <summary>
/// Законопроект: какой закон и вариант, на какой стадии (drafting — готовит министерство, passed — принят,
/// rejected — отклонён, withdrawn — отозван), когда начат и когда будет готов к голосованию, итог голосования.
/// Support — одобрение в обществе (0–1) на момент голосования.
/// </summary>
public sealed class Bill
{
    public string Law { get; set; } = "";
    public string Variant { get; set; } = "";
    public string Stage { get; set; } = "drafting";
    public DateOnly Started { get; set; }
    public DateOnly Ready { get; set; }
    public DateOnly? Decided { get; set; }
    public double Support { get; set; }
    public VoteResult? Vote { get; set; }
}

/// <summary>Переходный период: эффект закона плавно меняется от варианта From к варианту To за Months месяцев.</summary>
public sealed class LawTransition
{
    public string Law { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public DateOnly Begin { get; set; }
    public int Months { get; set; }

    public double Progress(DateOnly date)
    {
        if (Months <= 0)
            return 1;
        var months = (date.Year - Begin.Year) * 12 + date.Month - Begin.Month;
        return Math.Clamp((double)months / Months, 0, 1);
    }
}

public sealed class WorldLaws
{
    public Dictionary<string, CountryLaws> Countries { get; set; } = new(StringComparer.Ordinal);

    public CountryLaws this[string country] => Countries[country];
}
