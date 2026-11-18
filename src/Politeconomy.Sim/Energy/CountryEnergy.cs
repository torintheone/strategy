namespace Politeconomy.Sim.Energy;

/// <summary>
/// Электроэнергетика страны. Массивы по типам станций — в порядке data/energy.json: мощность и строящаяся мощность
/// (ГВт), выработка (ТВт·ч в год). Спрос и выработка — ТВт·ч в год; Shortage — доля спроса, которой не хватило
/// (веерные отключения); Cut — на сколько из-за этого падает выпуск каждой отрасли; Price — цена электричества
/// (100 на старте). MODEL.md, «Энергетика».
/// </summary>
public sealed class CountryEnergy
{
    public double[] Capacity { get; set; } = [];
    public double[] Capacity0 { get; set; } = [];
    public double[] Building { get; set; } = [];
    public double[] Generation { get; set; } = [];
    public double[] Share0 { get; set; } = [];
    /// <summary>Цена электричества от станций каждого типа на старте (для выбора, что строить).</summary>
    public double[] Cost0 { get; set; } = [];
    public double Demand { get; set; }
    public double Demand0 { get; set; }
    /// <summary>Чистый импорт — доля спроса (минус — экспорт), как в данных.</summary>
    public double ImportShare { get; set; }
    public double Imports { get; set; }
    public double Shortage { get; set; }
    /// <summary>Ожидаемый рост спроса в год.</summary>
    public double Growth { get; set; }
    public double Funding { get; set; } = 1;
    public double Price { get; set; } = 100;
    public double AverageCost0 { get; set; }
    /// <summary>Цена от станций (без надбавки за нехватку) к стартовой.</summary>
    public double CostRatio { get; set; } = 1;
    /// <summary>Вода для ГЭС в этом году (1 — обычный год) и за какой год она выпала.</summary>
    public double Hydro { get; set; } = 1;
    public int HydroYear { get; set; }
    public double Access { get; set; }
    public double Access0 { get; set; }
    public double Households { get; set; }
    public double Business0 { get; set; }
    public double Population0 { get; set; }
    public double Income0 { get; set; } = 1;
    public double Spending0 { get; set; }
    public double Investment0 { get; set; }
    public double Corruption0 { get; set; }
    public double Pollution0 { get; set; }
    public double[] Cut { get; set; } = [];
    public double[] Mood { get; set; } = [];
    public Dictionary<string, double> Effects { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorldEnergy
{
    public Dictionary<string, CountryEnergy> Countries { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Цена электричества в мире к стартовой (средняя по спросу, за прошлый месяц).</summary>
    public double CostRatio { get; set; } = 1;

    public CountryEnergy this[string country] => Countries[country];
}
