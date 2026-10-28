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
    IReadOnlyDictionary<string, StatRecord> Stats);

public sealed record StatRecord(
    double Value,
    int? Year,
    string Source,
    string? Method = null,
    int? N = null);

public sealed record PoliticsFile(
    IReadOnlyDictionary<string, string> Systems,
    IReadOnlyList<PoliticsRecord> Countries);

public sealed record PoliticsRecord(string Id, string System, string Note, bool Reviewed);

public sealed record ScenarioFile(
    string Id,
    string Name,
    string Description,
    DateOnly StartDate,
    ulong Seed,
    int WarmupMonths,
    string? PlayerCountry);

public sealed record BalanceFile(TimeBalance Time);

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
    string? Hotkey = null);

public sealed record IncomeGroupDef(string Id, string Name, string Color);

public sealed record WorldMapFile(
    string Projection,
    double Scale,
    double Width,
    double MinY,
    double MaxY,
    int PoliticalColors,
    IReadOnlyList<MapCountryRecord> Countries,
    IReadOnlyList<MapTerritoryRecord> Territories);

public sealed record MapCountryRecord(
    string Id,
    IReadOnlyList<double> Label,
    int Color,
    IReadOnlyList<string> Neighbors,
    IReadOnlyList<MapPolygonRecord> Polygons);

public sealed record MapTerritoryRecord(
    string Id,
    string Name,
    string Note,
    IReadOnlyList<double> Label,
    IReadOnlyList<MapPolygonRecord> Polygons);

public sealed record MapPolygonRecord(
    IReadOnlyList<IReadOnlyList<float>> Rings,
    IReadOnlyList<int> Triangles);

public sealed record CitiesFile(IReadOnlyList<CityRecord> Cities);

public sealed record CityRecord(
    string Name,
    string Country,
    float X,
    float Y,
    long Population,
    bool Capital,
    int Rank);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(CountriesFile))]
[JsonSerializable(typeof(PoliticsFile))]
[JsonSerializable(typeof(ScenarioFile))]
[JsonSerializable(typeof(BalanceFile))]
[JsonSerializable(typeof(IndicatorsFile))]
[JsonSerializable(typeof(MapModesFile))]
[JsonSerializable(typeof(WorldMapFile))]
[JsonSerializable(typeof(CitiesFile))]
internal sealed partial class DataJsonContext : JsonSerializerContext;
