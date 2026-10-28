using System.Text.Json.Serialization;

namespace Politeconomy.Sim.Economy;

/// <summary>Мировая часть экономики: цены на сырьё, валюты, мировой цикл и экономики всех стран.</summary>
public sealed class WorldEconomy
{
    /// <summary>Сколько месяцев прошло с начала партии. Во время разогрева не растёт.</summary>
    public int Months { get; set; }

    /// <summary>Год начала партии — от него считается календарь модели.</summary>
    public int StartYear { get; set; }

    /// <summary>Идёт разогрев: случайные толчки выключены, календарь модели стоит.</summary>
    public bool Warmup { get; set; }

    /// <summary>Мировые цены сырья, на старте 1.</summary>
    public double OilPrice { get; set; } = 1;
    public double OresPrice { get; set; } = 1;
    public double FoodPrice { get; set; } = 1;
    public double[] OilHistory { get; set; } = Ones();
    public double[] OresHistory { get; set; } = Ones();
    public double[] FoodHistory { get; set; } = Ones();
    public int HistoryIndex { get; set; }

    /// <summary>Мировой разрыв выпуска и инфляция, взвешенные по ВВП в долларах.</summary>
    public double WorldGap { get; set; }
    public double WorldInflation { get; set; }

    /// <summary>ВВП на душу по ППС у лидера (США на старте) — ориентир для догоняющего роста.</summary>
    public double FrontierPpp { get; set; }

    public Dictionary<string, CurrencyState> Currencies { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, CountryEconomy> Countries { get; set; } = new(StringComparer.Ordinal);

    public CountryEconomy this[string country] => Countries[country];

    public void PushHistory()
    {
        OilHistory[HistoryIndex] = OilPrice;
        OresHistory[HistoryIndex] = OresPrice;
        FoodHistory[HistoryIndex] = FoodPrice;
        HistoryIndex = (HistoryIndex + 1) % OilHistory.Length;
    }

    static double[] Ones() => [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1];
}

/// <summary>
/// Валюта и её центробанк. Если валютой пользуются несколько стран, ставка и курс общие,
/// а инфляция и цикл для правила ставки усредняются по странам с весами по ВВП.
/// </summary>
public sealed class CurrencyState
{
    public string Id { get; set; } = "";
    public string Regime { get; set; } = "float";
    public string? Anchor { get; set; }
    public bool Reserve { get; set; }
    public List<string> Members { get; set; } = [];
    /// <summary>Страна с наибольшим ВВП среди пользователей валюты.</summary>
    public string Home { get; set; } = "";
    public double PolicyRate { get; set; }
    /// <summary>Нейтральная реальная ставка и цель по инфляции.</summary>
    public double Neutral { get; set; }
    public double Target { get; set; }
    /// <summary>Надбавка к ставке якорной валюты при фиксированном курсе.</summary>
    public double PegSpread { get; set; }
    /// <summary>Курс: местных единиц за доллар, на старте 1.</summary>
    public double ExchangeRate { get; set; } = 1;
    /// <summary>Логарифм реального курса к доллару: плюс — валюта дешевле, чем на старте.</summary>
    public double LogReal { get; set; }
    public double RealRateDifference { get; set; }

    // Средние по пользователям валюты (обновляются центробанком каждый месяц)
    public double Inflation { get; set; }
    public double Expectations { get; set; }
    public double Gap { get; set; }
    public double LogPrice { get; set; }

    [JsonIgnore]
    public bool IsFloating => Regime == "float";
}
