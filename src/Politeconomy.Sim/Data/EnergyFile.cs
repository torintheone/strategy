namespace Politeconomy.Sim.Data;

// Электроэнергетика: типы станций, стратегии, цена углерода, тарифы, электроёмкость отраслей. Поля описаны в
// data/README.md, смысл — в MODEL.md, «Энергетика».

public sealed record EnergyFile(
    IReadOnlyList<PlantRecord> Plants,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Strategies,
    IReadOnlyDictionary<string, double> CarbonPrices,
    IReadOnlyDictionary<string, double> Tariffs,
    IReadOnlyDictionary<string, double> Intensity);

/// <summary>
/// Тип электростанций. Stat — доля выработки в данных стран; CapacityFactor — сколько часов в году станция может
/// работать (доля); BuildYears и LifeYears — срок стройки и службы; Capex — постоянная часть цены электричества,
/// Fuel — сырьё топлива (oil, ores или null), FuelCost — топливная часть при стартовой цене сырья; Co2 — тонн CO₂ на
/// МВт·ч; Pollution — вклад в загрязнение (уголь — 1); Base — работает всегда (атом, гидро, ветер и солнце), остальные
/// догружаются по спросу; Weight — склонность строить; MinIncome — с какой группы дохода такие станции строят те, у
/// кого их не было; CostDecline — на сколько в год дешевеет (ветер и солнце); Resource — рента своего топлива
/// (показатель страны): у кого оно есть, те строят такие станции охотнее; CostPerGw — сколько стоит построить 1 ГВт
/// (млрд долларов; для строек игрока).
/// </summary>
public sealed record PlantRecord(
    string Id,
    string Name,
    string Stat,
    double CapacityFactor,
    double BuildYears,
    double LifeYears,
    double Capex,
    string? Fuel,
    double FuelCost,
    double Co2,
    double Pollution,
    bool Base,
    double Weight,
    int MinIncome = 0,
    double CostDecline = 0,
    string? Resource = null,
    double CostPerGw = 1);

// Коэффициенты энергетики из data/balance.json (раздел energy). Списки из четырёх чисел — по группам дохода.

public sealed class EnergyBalance
{
    /// <summary>Запас мощности сверх нужной выработки на старте и цель стройки.</summary>
    public IReadOnlyList<double> Reserve { get; init; } = [0.05, 0.1, 0.15, 0.2];
    /// <summary>Доля населения в спросе на электричество на старте; остальное — отрасли.</summary>
    public IReadOnlyList<double> Households { get; init; } = [0.45, 0.35, 0.28, 0.3];
    /// <summary>Спрос населения растёт с доходом на душу в степени HouseholdIncome.</summary>
    public double HouseholdIncome { get; init; } = 0.6;
    /// <summary>Энергоэффективность: электроёмкость падает на Efficiency в год.</summary>
    public double Efficiency { get; init; } = 0.01;
    /// <summary>Чистый импорт — доля спроса, не больше ImportMax, экспорт — не больше ExportMax; при нехватке соседи добавят до ImportFlex спроса.</summary>
    public double ImportMax { get; init; } = 0.6;
    public double ExportMax { get; init; } = 3;
    public double ImportFlex { get; init; } = 0.03;
    /// <summary>Стройка: недостаток мощности заказывается за ReserveMonths, недостаток строящегося — за PipelineMonths.</summary>
    public double ReserveMonths { get; init; } = 24;
    public double PipelineMonths { get; init; } = 24;
    /// <summary>Ожидаемый рост спроса сглаживается за GrowthMonths; не выше GrowthMax в год.</summary>
    public double GrowthMonths { get; init; } = 24;
    public double GrowthMax { get; init; } = 0.1;
    /// <summary>Денег хватает, чтобы заказать за год станций не больше чем на BuildMax × деньги от нужной выработки.</summary>
    public double BuildMax { get; init; } = 0.12;
    /// <summary>Деньги на стройку: (расходы на энергетику / стартовые)^FundingSpending × (инвестиции / стартовые)^FundingInvestment × (1 + законы и программы) × (1 − FundingCorruption × рост коррупции).</summary>
    public double FundingSpending { get; init; } = 1;
    public double FundingInvestment { get; init; } = 0.5;
    public double FundingCorruption { get; init; } = 0.01;
    public double SpendingRange { get; init; } = 2;
    public double FundingMin { get; init; } = 0.1;
    public double FundingMax { get; init; } = 3;
    /// <summary>Выбор новых станций: вес × (стартовая доля + Inertia) × стратегия × (стартовая цена / нынешняя)^CostElasticity (цена — с углеродом).</summary>
    public double Inertia { get; init; } = 0.2;
    public double CostElasticity { get; init; } = 1.5;
    /// <summary>Своё топливо (рента больше ResourceRent % ВВП) — станции на нём строят охотнее в 1 + ResourceBonus раз.</summary>
    public double ResourceRent { get; init; } = 0.3;
    public double ResourceBonus { get; init; } = 0.5;
    /// <summary>ГЭС — не больше HydroRoom стартовой мощности (реки кончаются); вода по годам — в среднем 1 с разбросом HydroVariation.</summary>
    public double HydroRoom { get; init; } = 1.5;
    public double HydroVariation { get; init; } = 0.06;
    public double HydroMin { get; init; } = 0.8;
    public double HydroMax { get; init; } = 1.15;
    /// <summary>Ветер и солнце нестабильны: выше доли IntermittentFrom в выработке их строят всё неохотнее (до нуля на + IntermittentRange).</summary>
    public double IntermittentFrom { get; init; } = 0.35;
    public double IntermittentRange { get; init; } = 0.35;
    /// <summary>Дешевеющие станции (ветер и солнце) дешевеют не ниже CostFloor от стартовой цены: дальше упираются в накопители и сети.</summary>
    public double CostFloor { get; init; } = 0.4;
    /// <summary>Цена углерода в цене электричества: CarbonCost × цена (из закона) × CO₂ (т/МВт·ч).</summary>
    public double CarbonCost { get; init; } = 60;
    /// <summary>Нехватка поднимает цену: × (1 + ShortagePrice × нехватка).</summary>
    public double ShortagePrice { get; init; } = 2;
    /// <summary>Веерные отключения: выпуск отрасли × (1 − PowerCut × нехватка × электроёмкость / средняя), не меньше 1 − CutMax.</summary>
    public double PowerCut { get; init; } = 0.8;
    public double CutMax { get; init; } = 0.5;
    /// <summary>Настроение: −BlackoutMood за процент нехватки; −TariffMood × доля цены, которую платят люди × рост цены (%).</summary>
    public double BlackoutMood { get; init; } = 0.5;
    public double TariffMood { get; init; } = 0.08;
    /// <summary>Электричество, подорожавшее сильнее, чем в среднем по миру, тормозит рост: −PriceGrowth п. п. × ln(цена к стартовой / мировая к стартовой).</summary>
    public double PriceGrowth { get; init; } = 0.3;
    /// <summary>Загрязнение: × (1 + PollutionShare × изменение Σ доля × вклад станции).</summary>
    public double PollutionShare { get; init; } = 0.8;
    /// <summary>Доступ к электричеству: разрыв до 100% закрывается на AccessRate в год (× деньги, × (1 + программы)).</summary>
    public double AccessRate { get; init; } = 0.04;
}
