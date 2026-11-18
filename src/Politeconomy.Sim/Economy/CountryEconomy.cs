using System.Text.Json.Serialization;

namespace Politeconomy.Sim.Economy;

/// <summary>
/// Экономика одной страны. Деньги — в «местных единицах»: на старте одна единица равна доллару,
/// дальше её стоимость меняется с курсом. Потоки (ВВП, доходы, расходы) — в годовом выражении.
/// Доли и ставки — дроби (0,02 = 2%). Массивы по отраслям идут в порядке data/industries.json.
/// </summary>
public sealed class CountryEconomy
{
    // --- Калибровка: задаётся на старте из данных
    public string Currency { get; set; } = "";
    public int IncomeIndex { get; set; }
    public double[] EmploymentShare { get; set; } = [];
    public double[] InvestmentShare { get; set; } = [];
    public double[] BaseOutput { get; set; } = [];
    public double RealGdp0 { get; set; }
    public double Population0 { get; set; }
    public double Ppp0 { get; set; }
    public double TrendGrowth { get; set; }
    public double FirstYearGrowth { get; set; }
    public double PopulationGrowth0 { get; set; }
    /// <summary>
    /// Естественная безработица и цель по инфляции — текущие. За ForecastYears лет они плавно идут
    /// от стартовых значений (данные) к прогнозу МВФ на 2030 год: модель стартует в равновесии с данными.
    /// </summary>
    public double NaturalUnemployment { get; set; }
    public double NaturalUnemploymentStart { get; set; }
    public double NaturalUnemploymentLong { get; set; }
    public double InflationTarget { get; set; }
    public double InflationStart { get; set; }
    public double InflationLong { get; set; }
    /// <summary>Нейтральная реальная ставка для страны: при ней ставка ЦБ не разгоняет и не тормозит экономику.</summary>
    public double Neutral { get; set; }
    public double DebtRatio0 { get; set; }
    public double Credibility { get; set; }
    public double InvestmentRate0 { get; set; }
    public double Exports0 { get; set; }
    public double Imports0 { get; set; }
    public double CurrentAccount0 { get; set; }
    public double Openness { get; set; }
    /// <summary>Чистая выручка от нефти и газа, руд и угля в долях ВВП: рента минус типичное потребление.</summary>
    public double OilNet { get; set; }
    public double OresNet { get; set; }
    /// <summary>Доля сырья в экспорте и доля нефти в импорте.</summary>
    public double CommodityExports { get; set; }
    public double OilImports { get; set; }
    public double FoodWeight { get; set; }
    public double ForeignDebtShare { get; set; }
    /// <summary>Доля долга, ставка по которой следует за рынком; остальное — льготные и старые займы.</summary>
    public double MarketDebtShare { get; set; }
    /// <summary>Средняя ставка по долгу и рыночная ставка на старте: льготная часть долга сдвигается только вместе с рынком.</summary>
    public double EffectiveRate0 { get; set; }
    public double MarketRate0 { get; set; }
    public double Spread0 { get; set; }
    public double DebtAnchor { get; set; }
    /// <summary>Ставка каждого дохода бюджета к его базе (налог на зарплаты — к фонду оплаты и т. д.).</summary>
    public double[] RevenueRate { get; set; } = [];
    /// <summary>Налоговая политика: множитель к ставке каждого дохода (1 — как на старте). Меняется бюджетом игрока.</summary>
    public double[] TaxPolicy { get; set; } = [];
    /// <summary>Год, в котором парламент так и не принял бюджет (рейтинг в этот год ниже), 0 — не было.</summary>
    public int BudgetFailedYear { get; set; }
    /// <summary>Доля мира в ВВП (для подсчёта мирового цикла без самой страны).</summary>
    public double WorldShare { get; set; }

    // --- Производство
    public double[] Tfp { get; set; } = [];
    public double[] Capital { get; set; } = [];
    public double[] Potential { get; set; } = [];
    public double[] Output { get; set; } = [];
    public double PotentialGrowth { get; set; }
    public double Population { get; set; }
    public double LaborForce { get; set; }

    // --- Спрос и цикл
    public double GapCore { get; set; }
    public double Gap { get; set; }
    public double FiscalImpulse { get; set; }
    public double PrimaryDeficitSmooth { get; set; }
    /// <summary>Стройки не из бюджета (кредит, концессия) — доля ВВП в год: дают спросу такой же толчок, как дефицит бюджета.</summary>
    public double ProjectFlow { get; set; }
    public double InvestmentRate { get; set; }
    public double Exports { get; set; }
    public double Imports { get; set; }
    public double CurrentAccount { get; set; }

    // --- Труд
    public double Unemployment { get; set; }
    public double WageIndex { get; set; } = 1;

    // --- Цены, ставка, курс
    public double Price { get; set; } = 1;
    public double Inflation { get; set; }
    public double Expectations { get; set; }
    public double InflationPull { get; set; }
    public double DemandPart { get; set; }
    public double ImportPart { get; set; }
    public double CommodityPart { get; set; }
    public double PolicyRate { get; set; }
    public double ExchangeRate { get; set; } = 1;
    public double RealExchangeRate { get; set; } = 1;

    // --- Итоги месяца
    public double RealGdp { get; set; }
    public double NominalGdp { get; set; }
    public double ResourceGdp { get; set; }

    // --- Бюджет
    public double[] Revenue { get; set; } = [];
    public double[] Spending { get; set; } = [];
    /// <summary>Плановые расходы по статьям в долях «структурного» номинального ВВП (без цикла). Меняются раз в год.</summary>
    public double[] SpendingPlan { get; set; } = [];
    public double Interest { get; set; }
    public double Debt { get; set; }
    public double EffectiveRate { get; set; }
    public double MarketRate { get; set; }
    public double PrimaryBalancePlan { get; set; }
    public double Rating { get; set; }
    /// <summary>Сколько раз страна объявляла дефолт и в каком году последний раз (0 — не было).</summary>
    public int Defaults { get; set; }
    public int LastDefaultYear { get; set; }
    public double Spread { get; set; }

    // --- История за 12 месяцев: значение год назад лежит в ячейке HistoryIndex
    public double[] RealGdpHistory { get; set; } = new double[12];
    public double[] PriceHistory { get; set; } = new double[12];
    public double[] ExchangeHistory { get; set; } = new double[12];
    public int HistoryIndex { get; set; }

    [JsonIgnore]
    public double TotalRevenue => Revenue.Sum();
    [JsonIgnore]
    public double TotalSpending => Spending.Sum();
    /// <summary>Номинальный ВВП без циклического отклонения — от него считаются плановые расходы.</summary>
    [JsonIgnore]
    public double StructuralGdp => NominalGdp / (1 + Gap);
    [JsonIgnore]
    public double GdpUsd => NominalGdp / ExchangeRate;
    [JsonIgnore]
    public double DebtRatio => NominalGdp > 0 ? Debt / NominalGdp : 0;
    [JsonIgnore]
    public double BalanceRatio => NominalGdp > 0 ? (TotalRevenue - TotalSpending - Interest) / NominalGdp : 0;

    public double YearAgo(double[] history) => history[HistoryIndex];

    /// <summary>Записать итоги месяца в историю. Вызывается один раз в конце месяца.</summary>
    public void PushHistory()
    {
        RealGdpHistory[HistoryIndex] = RealGdp;
        PriceHistory[HistoryIndex] = Price;
        ExchangeHistory[HistoryIndex] = ExchangeRate;
        HistoryIndex = (HistoryIndex + 1) % RealGdpHistory.Length;
    }
}
