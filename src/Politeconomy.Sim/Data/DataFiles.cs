using System.Text.Json;
using System.Text.Json.Serialization;

namespace Politeconomy.Sim.Data;

// Формы JSON-файлов из папки data/. Поля описаны в data/README.md.

public sealed record CountriesFile(
    string Built,
    IReadOnlyDictionary<string, string> Sources,
    IReadOnlyList<CountryRecord> Countries);

public sealed record CountryRecord(
    string Id,
    string Iso2,
    string Name,
    string ShortName,
    string OfficialName,
    string NameEn,
    string Capital,
    string Region,
    string Subregion,
    double? AreaKm2,
    bool Landlocked,
    string IncomeGroup,
    string IncomeSource,
    IReadOnlyDictionary<string, StatRecord> Stats,
    bool UnMember = true,
    string? StatusNote = null,
    string Currency = "");

public sealed record StatRecord(
    double Value,
    int? Year,
    string Source,
    string? Method = null,
    int? N = null);

public sealed record RegionsFile(
    string Built,
    IReadOnlyDictionary<string, string> Sources,
    IReadOnlyList<RegionRecord> Regions);

/// <summary>Регион страны. Country — страна, которой регион принадлежит по международному праву.</summary>
public sealed record RegionRecord(
    string Id,
    string Name,
    string Country,
    string Capital,
    double Population,
    string PopulationSource,
    double AreaKm2,
    int Units);

/// <summary>Стартовый контроль регионов, если он отличается от принадлежности.</summary>
public sealed record ControlFile(IReadOnlyList<ControlRecord> Control);

public sealed record ControlRecord(string Region, string Controller, string Note);

public sealed record PoliticsFile(
    IReadOnlyDictionary<string, string> Systems,
    IReadOnlyList<PoliticsRecord> Countries,
    IReadOnlyDictionary<string, string>? Regimes = null);

/// <summary>
/// Политика страны: тип системы, режим (democracy, hybrid, authoritarian), избирательная система парламента
/// (majoritarian, proportional, mixed, none) и барьер в процентах, есть ли легальные партии, особое семейство партии
/// власти, календарь выборов (срок парламента и ближайшие выборы «год-месяц»; у президентских и смешанных
/// республик — срок президента, ближайшие выборы и есть ли второй тур), пометка и проверено ли человеком.
/// </summary>
public sealed record PoliticsRecord(
    string Id,
    string System,
    string Note,
    bool Reviewed,
    string Regime = "democracy",
    string Electoral = "proportional",
    double Threshold = 3,
    bool Parties = true,
    string? RulingFamily = null,
    int Term = 5,
    string? NextElection = null,
    int? PresidentTerm = null,
    string? NextPresident = null,
    bool TwoRounds = true);

public sealed record ScenarioFile(
    string Id,
    string Name,
    string Description,
    DateOnly StartDate,
    ulong Seed,
    int WarmupMonths,
    string? PlayerCountry);

public sealed record BalanceFile(TimeBalance Time, EconomyBalance? Economy = null, DemographyBalance? Demography = null,
    SocietyBalance? Society = null, GovernmentBalance? Government = null, LawsBalance? Laws = null,
    BudgetCycleBalance? BudgetCycle = null, PoliticsBalance? Politics = null, InterestGroupsBalance? InterestGroups = null,
    UnrestBalance? Unrest = null);

public sealed record TimeBalance(IReadOnlyList<double> DaysPerSecond);

public sealed record IndicatorsFile(IReadOnlyList<IndicatorDef> Indicators);

public sealed record IndicatorDef(string Id, string Name, string Format, string Description, string? Unit = null);

public sealed record MapModesFile(
    MapPalettes Palettes,
    IReadOnlyList<MapModeDef> Modes,
    IReadOnlyDictionary<string, string> PoliticsColors,
    IReadOnlyList<IncomeGroupDef> IncomeGroups);

public sealed record MapPalettes(
    IReadOnlyList<string> Political,
    IReadOnlyList<string> Sequential,
    IReadOnlyList<string> DivergingNegative,
    string DivergingNeutral,
    IReadOnlyList<string> DivergingPositive,
    string NoData,
    string Territory);

public sealed record MapModeDef(
    string Id,
    string Name,
    string ShortName,
    string Kind,
    string? Indicator = null,
    IReadOnlyList<double>? Thresholds = null,
    string? Hotkey = null,
    string? Unit = null);

public sealed record IncomeGroupDef(string Id, string Name, string Color);

public sealed record WorldMapFile(
    string Projection,
    double Scale,
    double Width,
    double MinY,
    double MaxY,
    int PoliticalColors,
    IReadOnlyList<MapCountryRecord> Countries,
    IReadOnlyList<MapRegionRecord> Regions,
    IReadOnlyList<MapTerritoryRecord> Territories,
    MapLinesRecord Lines);

/// <summary>Страна на карте: точка подписи, цвет политической карты, соседи и контур для подсветки.</summary>
public sealed record MapCountryRecord(
    string Id,
    IReadOnlyList<double> Label,
    int Color,
    IReadOnlyList<string> Neighbors,
    IReadOnlyList<IReadOnlyList<float>> Outline);

/// <summary>Регион на карте. Country — страна-владелец; Territory — нейтральная территория (тогда Country пустой).</summary>
public sealed record MapRegionRecord(
    string Id,
    string? Country,
    string? Territory,
    IReadOnlyList<double> Label,
    IReadOnlyList<MapPolygonRecord> Polygons);

public sealed record MapTerritoryRecord(
    string Id,
    string Name,
    string Note,
    IReadOnlyList<double> Label);

/// <summary>Линии карты: побережье, границы стран, границы регионов внутри стран.</summary>
public sealed record MapLinesRecord(
    IReadOnlyList<IReadOnlyList<float>> Coast,
    IReadOnlyList<IReadOnlyList<float>> Border,
    IReadOnlyList<IReadOnlyList<float>> Internal);

public sealed record MapPolygonRecord(
    IReadOnlyList<IReadOnlyList<float>> Rings,
    IReadOnlyList<int> Triangles);

public sealed record CitiesFile(IReadOnlyList<CityRecord> Cities);

public sealed record CityRecord(
    string Name,
    string Country,
    string Region,
    float X,
    float Y,
    long Population,
    bool Capital,
    bool RegionalCapital,
    int Rank);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(CountriesFile))]
[JsonSerializable(typeof(PoliticsFile))]
[JsonSerializable(typeof(RegionsFile))]
[JsonSerializable(typeof(ControlFile))]
[JsonSerializable(typeof(ScenarioFile))]
[JsonSerializable(typeof(BalanceFile))]
[JsonSerializable(typeof(IndicatorsFile))]
[JsonSerializable(typeof(MapModesFile))]
[JsonSerializable(typeof(WorldMapFile))]
[JsonSerializable(typeof(CitiesFile))]
[JsonSerializable(typeof(CurrenciesFile))]
[JsonSerializable(typeof(IndustriesFile))]
[JsonSerializable(typeof(BudgetFile))]
[JsonSerializable(typeof(PopulationFile))]
[JsonSerializable(typeof(ClassesFile))]
[JsonSerializable(typeof(BenchmarksFile))]
[JsonSerializable(typeof(GovernmentFile))]
[JsonSerializable(typeof(LawsFile))]
[JsonSerializable(typeof(HistoryFile))]
[JsonSerializable(typeof(PartiesFile))]
[JsonSerializable(typeof(InterestGroupsFile))]
[JsonSerializable(typeof(CultureRecord))]
internal sealed partial class DataJsonContext : JsonSerializerContext;
