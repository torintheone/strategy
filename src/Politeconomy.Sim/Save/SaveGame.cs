using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Save;

/// <summary>Сохранение партии: состояние мира в JSON, сжатое gzip. Постоянные данные берутся из data/.</summary>
public static class SaveGame
{
    public const int FormatVersion = 9;

    public static SaveState Capture(World world) => new(
        FormatVersion,
        world.Data.Scenario.Id,
        world.Date,
        world.Seed,
        world.Random.State,
        world.PlayerCountry,
        world.Countries
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => new CountryState(
                c.Id,
                WorldFactory.SystemId(c.System),
                WorldFactory.IncomeId(c.Income),
                c.Stats
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new StatState(kv.Key, kv.Value.Value, kv.Value.Year, kv.Value.Source, kv.Value.Method, kv.Value.N,
                        kv.Value.Factors?.ToList()))
                    .ToList(),
                WorldFactory.RegimeId(c.Regime)))
            .ToList(),
        world.Regions
            .Where(r => r.IsOccupied)
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new RegionState(r.Id, r.Controller))
            .ToList(),
        world.Economy,
        world.Demography,
        world.Society,
        world.Government,
        world.Journal,
        world.Laws,
        world.Budgets,
        world.History,
        world.Politics);

    public static void Write(World world, Stream output)
    {
        using var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
        JsonSerializer.Serialize(gzip, Capture(world), SaveJsonContext.Default.SaveState);
    }

    public static void Write(World world, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var file = File.Create(temp))
            Write(world, file);
        File.Move(temp, path, overwrite: true);
    }

    public static World Read(GameData data, Stream input)
    {
        using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
        var state = JsonSerializer.Deserialize(gzip, SaveJsonContext.Default.SaveState)
            ?? throw new DataException("Файл сохранения пустой");
        return Restore(data, state);
    }

    public static World Read(GameData data, string path)
    {
        using var file = File.OpenRead(path);
        return Read(data, file);
    }

    public static World Restore(GameData data, SaveState state)
    {
        if (state.Version != FormatVersion)
            throw new DataException($"Сохранение версии {state.Version}, игра понимает версию {FormatVersion}");
        var records = data.Countries.Countries.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var countries = new List<Country>();
        foreach (var cs in state.Countries)
        {
            if (!records.TryGetValue(cs.Id, out var record))
                throw new DataException($"В сохранении страна {cs.Id}, которой нет в данных");
            var country = new Country
            {
                Id = cs.Id,
                Info = record,
                System = WorldFactory.ParseSystem(cs.System),
                Income = WorldFactory.ParseIncome(cs.Income),
                Regime = WorldFactory.ParseRegime(cs.Regime),
            };
            foreach (var s in cs.Stats)
                country.Stats[s.Key] = new StatValue(s.Value, s.Year, s.Source, s.Method, s.N, s.Factors);
            countries.Add(country);
        }
        var controllers = (state.Occupied ?? []).ToDictionary(r => r.Id, r => r.Controller, StringComparer.Ordinal);
        var regions = data.Regions.Regions
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new Region { Id = r.Id, Info = r, Owner = r.Country, Controller = controllers.GetValueOrDefault(r.Id, r.Country) })
            .ToList();
        foreach (var id in controllers.Keys.Where(id => regions.All(r => r.Id != id)))
            throw new DataException($"В сохранении регион {id}, которого нет в данных");
        return new World
        {
            Data = data,
            Date = state.Date,
            Seed = state.Seed,
            Random = DeterministicRandom.FromState(state.Rng),
            PlayerCountry = state.PlayerCountry,
            Countries = countries,
            Regions = regions,
            Economy = state.Economy,
            Demography = state.Demography,
            Society = state.Society,
            Government = state.Government,
            Journal = state.Journal ?? [],
            Laws = state.Laws,
            Budgets = state.Budgets,
            History = state.History,
            Politics = state.Politics,
        };
    }

    /// <summary>Отпечаток состояния: одинаковые миры дают одинаковую строку. Нужен для тестов детерминизма.</summary>
    public static string Hash(World world)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Capture(world), SaveJsonContext.Default.SaveState);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

public sealed record SaveState(
    int Version,
    string Scenario,
    DateOnly Date,
    ulong Seed,
    ulong[] Rng,
    string? PlayerCountry,
    List<CountryState> Countries,
    List<RegionState>? Occupied = null,
    Economy.WorldEconomy? Economy = null,
    Demography.WorldDemography? Demography = null,
    Society.WorldSociety? Society = null,
    Government.WorldGovernment? Government = null,
    List<JournalEntry>? Journal = null,
    Laws.WorldLaws? Laws = null,
    Budget.WorldBudgets? Budgets = null,
    History.WorldHistory? History = null,
    Politics.WorldPolitics? Politics = null);

/// <summary>Регион, который контролирует не владелец.</summary>
public sealed record RegionState(string Id, string Controller);

public sealed record CountryState(string Id, string System, string Income, List<StatState> Stats, string Regime = "democracy");

public sealed record StatState(string Key, double Value, int? Year, string Source, string? Method, int? N,
    List<StatFactor>? Factors = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SaveState))]
internal sealed partial class SaveJsonContext : JsonSerializerContext;
