namespace Politeconomy.Sim.Data;

// Коэффициенты модели экономики из data/balance.json (раздел economy). Смысл каждого — в MODEL.md.
// Значения по умолчанию совпадают с файлом: если коэффициент убрать из файла, модель возьмёт их.
// Списки из четырёх чисел — по группам дохода: низкий, ниже среднего, выше среднего, высокий.

public sealed class EconomyBalance
{
    public SupplyBalance Supply { get; init; } = new();
    public DemandBalance Demand { get; init; } = new();
    public LaborBalance Labor { get; init; } = new();
    public PriceBalance Prices { get; init; } = new();
    public MonetaryBalance Monetary { get; init; } = new();
    public ExchangeBalance Exchange { get; init; } = new();
    public CommodityBalance Commodities { get; init; } = new();
    public FiscalBalance Budget { get; init; } = new();
    public RatingBalance Rating { get; init; } = new();

    /// <summary>Значение для группы дохода из списка «низкий … высокий».</summary>
    public static double ByIncome(IReadOnlyList<double> values, int group) => values[Math.Clamp(group, 0, values.Count - 1)];
}

public sealed class SupplyBalance
{
    public double FrontierGrowth { get; init; } = 0.012;
    public double ConvergenceSpeed { get; init; } = 0.012;
    public double Institutions { get; init; } = 0.6;
    public double CatchUpMin { get; init; } = -0.005;
    public double CatchUpMax { get; init; } = 0.03;
    public double ForecastYears { get; init; } = 5;
    public double ForecastFadeYears { get; init; } = 8;
    public double FirstYearFadeYears { get; init; } = 1;
    public double FirstYearBand { get; init; } = 0.02;
    public double TrendMin { get; init; } = -0.01;
    public double TrendMax { get; init; } = 0.08;
    public double PopulationHalfLifeYears { get; init; } = 40;
    public double UserCostMin { get; init; } = 0.005;
    public double UserCostMax { get; init; } = 1.0;
}

public sealed class DemandBalance
{
    public double GapPersistence { get; init; } = 0.95;
    public double RateSensitivity { get; init; } = 0.025;
    public double RealRateGapLimit { get; init; } = 0.1;
    public double FiscalMultiplier { get; init; } = 0.6;
    public double FiscalImpulseMonths { get; init; } = 24;
    public double WorldSpillover { get; init; } = 0.3;
    public double CommodityIncome { get; init; } = 0.25;
    public double OilConsumption { get; init; } = 0.025;
    public double OresConsumption { get; init; } = 0.005;
    public double RealExchangeEffect { get; init; } = 0.1;
    public double GapMin { get; init; } = -0.2;
    public double GapMax { get; init; } = 0.15;
    public IReadOnlyList<double> Shock { get; init; } = [0.006, 0.005, 0.004, 0.003];
    public double InvestmentCycle { get; init; } = 2.0;
    public double InvestmentRate { get; init; } = 3.0;
    public double InvestmentMin { get; init; } = 0.4;
    public double InvestmentMax { get; init; } = 1.6;
    public double ExportElasticity { get; init; } = 0.6;
    public double ImportElasticity { get; init; } = 0.6;
    public double ImportCycle { get; init; } = 1.5;
    public double CommodityExports { get; init; } = 1.3;
    public double CurrentAccountLimit { get; init; } = 0.6;
}

public sealed class LaborBalance
{
    public double Okun { get; init; } = 0.4;
    public double Adjustment { get; init; } = 0.15;
    public double NaturalMin { get; init; } = 0.02;
    public double NaturalMax { get; init; } = 0.35;
    public double NaturalMaxShift { get; init; } = 0.05;
    public double WageSlack { get; init; } = 0.5;
}

public sealed class PriceBalance
{
    public double Adjustment { get; init; } = 0.15;
    public double Expectations { get; init; } = 0.06;
    public double DemandPressure { get; init; } = 0.35;
    public double ImportPassThrough { get; init; } = 0.25;
    public double WorldInflation { get; init; } = 0.025;
    public IReadOnlyList<double> FoodWeight { get; init; } = [0.4, 0.3, 0.2, 0.12];
    public double FoodPassThrough { get; init; } = 0.3;
    public double EnergyWeight { get; init; } = 0.08;
    public double EnergyPassThrough { get; init; } = 0.1;
    public double Shock { get; init; } = 0.002;
    public IReadOnlyList<double> Credibility { get; init; } = [0.4, 0.5, 0.6, 0.75];
    public double AnchorMin { get; init; } = 0.01;
    public double AnchorMax { get; init; } = 0.4;
    public double InflationMin { get; init; } = -0.05;
    public double InflationMax { get; init; } = 3.0;
}

public sealed class MonetaryBalance
{
    public double TaylorInflation { get; init; } = 0.5;
    public double TaylorGap { get; init; } = 0.5;
    public double Smoothing { get; init; } = 0.15;
    public IReadOnlyList<double> NeutralRate { get; init; } = [0.03, 0.025, 0.015, 0.005];
    public double NeutralBelow { get; init; } = 0.02;
    public double NeutralAbove { get; init; } = 0.04;
    public double Floor { get; init; } = -0.005;
    public double Ceiling { get; init; } = 1.0;
    public double PegSpread { get; init; } = 0.01;
    public double PegInflationGap { get; init; } = 0.1;
}

public sealed class ExchangeBalance
{
    public double Reversion { get; init; } = 0.02;
    public double RateDifferential { get; init; } = 2.0;
    public double CommodityCurrency { get; init; } = 0.5;
    public IReadOnlyList<double> Shock { get; init; } = [0.025, 0.025, 0.025, 0.02];
}

public sealed class CommodityBalance
{
    public double Reversion { get; init; } = 0.03;
    public double WorldDemand { get; init; } = 1.5;
    public double OilShock { get; init; } = 0.07;
    public double OresShock { get; init; } = 0.05;
    public double FoodShock { get; init; } = 0.035;
}

public sealed class FiscalBalance
{
    public double DebtMaturityMonths { get; init; } = 84;
    public double TermPremium { get; init; } = 0.01;
    public double ForeignPremium { get; init; } = 0.005;
    public IReadOnlyList<double> DebtAnchor { get; init; } = [0.4, 0.45, 0.5, 0.6];
    public double DebtCorrection { get; init; } = 0.03;
    public double TargetMin { get; init; } = -0.03;
    public double TargetMax { get; init; } = 0.05;
    public double ConsolidationSpeed { get; init; } = 0.3;
    public double MaxStep { get; init; } = 0.005;
    public double LargeGapShare { get; init; } = 0.25;
    public IReadOnlyList<double> ForeignDebtShare { get; init; } = [0.6, 0.45, 0.3, 0.1];
    public IReadOnlyList<double> MarketDebtShare { get; init; } = [0.1, 0.3, 0.6, 1.0];
    public double InterestRevenueCap { get; init; } = 0.3;
    public double ResourceTake { get; init; } = 0.5;
    public double ResourceMaxShare { get; init; } = 0.7;
    public IReadOnlyList<double> Stabilizers { get; init; } = [0.03, 0.08, 0.15, 0.25];
    public double SpendingFloor { get; init; } = 0.05;
    public double RateCap { get; init; } = 0.4;
    public double DefaultInterestShare { get; init; } = 0.35;
    /// <summary>Дефолт и тогда, когда номинальные проценты больше такой доли доходов (высокая инфляция их не спасает).</summary>
    public double DefaultNominalShare { get; init; } = 0.8;
    public IReadOnlyList<double> DefaultDebt { get; init; } = [1.5, 1.8, 2.2, 3.0];
    public double DefaultDebtGrowth { get; init; } = 1.3;
    public double Haircut { get; init; } = 0.5;
    public double DefaultRecession { get; init; } = 0.04;
    public double DefaultRatingYears { get; init; } = 5;
}

public sealed class RatingBalance
{
    public double Base { get; init; } = 14.5;
    public double WealthReference { get; init; } = 30000;
    public double Wealth { get; init; } = 3.5;
    public double Size { get; init; } = 0.8;
    public double DebtThreshold { get; init; } = 0.6;
    public double Debt { get; init; } = 2.0;
    public double DeficitThreshold { get; init; } = 0.03;
    public double Deficit { get; init; } = 30;
    public double InflationThreshold { get; init; } = 0.05;
    public double Inflation { get; init; } = 2.0;
    public double ReserveCurrency { get; init; } = 2.0;
    public double DollarBonus { get; init; } = 2.0;
    public double SpreadBase { get; init; } = 0.002;
    public double SpreadSlope { get; init; } = 0.25;
    public double SpreadMax { get; init; } = 0.15;
    /// <summary>Год без принятого бюджета снижает рейтинг на столько ступеней.</summary>
    public double BudgetFailure { get; init; } = 1.0;
    public double WealthFloor { get; init; } = -6;
    public double SizeFloor { get; init; } = -3;
}
