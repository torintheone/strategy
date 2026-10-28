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
    public const int FormatVersion = 1;

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
                    .Select(kv => new StatState(kv.Key, kv.Value.Value, kv.Value.Year, kv.Value.Source, kv.Value.Method, kv.Value.N))
                    .ToList()))
            .ToList());

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
            };
            foreach (var s in cs.Stats)
                country.Stats[s.Key] = new StatValue(s.Value, s.Year, s.Source, s.Method, s.N);
            countries.Add(country);
        }
        return new World
        {
            Data = data,
            Date = state.Date,
            Seed = state.Seed,
            Random = DeterministicRandom.FromState(state.Rng),
            PlayerCountry = state.PlayerCountry,
            Countries = countries,
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
    List<CountryState> Countries);

public sealed record CountryState(string Id, string System, string Income, List<StatState> Stats);

public sealed record StatState(string Key, double Value, int? Year, string Source, string? Method, int? N);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SaveState))]
internal sealed partial class SaveJsonContext : JsonSerializerContext;
