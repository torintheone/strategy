namespace Politeconomy.Sim.Data;

// Справочники экономики: валюты, отрасли, статьи бюджета. Поля описаны в data/README.md.

public sealed record CurrenciesFile(IReadOnlyList<CurrencyRecord> Currencies);

/// <summary>
/// Валюта. Regime: float — плавающий курс и своя ставка; peg — курс и ставка следуют за валютой Anchor.
/// Валюта, которой нет в файле, считается плавающей.
/// </summary>
public sealed record CurrencyRecord(
    string Id,
    string Name,
    string Regime,
    string? Anchor = null,
    bool Reserve = false,
    string? Bank = null);

public sealed record IndustriesFile(IReadOnlyList<IndustryRecord> Industries);

/// <summary>
/// Отрасль. Sector — часть ВВП по Всемирному банку (agriculture, industry, services);
/// Kind — commodity, manufactured, service или public. Alpha — доля капитала в доходе.
/// </summary>
public sealed record IndustryRecord(
    string Id,
    string Name,
    string Sector,
    string Kind,
    double Cycle,
    double Alpha,
    double Depreciation,
    IReadOnlyList<double> Skills,
    IReadOnlyDictionary<string, double>? ManufacturingShare = null,
    double? ServicesShare = null,
    bool MarketService = false);

public sealed record BudgetFile(IReadOnlyList<RevenueRecord> Revenues, IReadOnlyList<SpendingRecord> Spending);

/// <summary>Статья доходов: от какой базы считается и насколько чувствительна к циклу.</summary>
public sealed record RevenueRecord(
    string Id,
    string Name,
    string Base,
    double Elasticity,
    IReadOnlyDictionary<string, double> Shares);

/// <summary>Статья расходов и её типичная доля в расходах без процентов по группам дохода.</summary>
public sealed record SpendingRecord(
    string Id,
    string Name,
    IReadOnlyDictionary<string, double> Shares);
