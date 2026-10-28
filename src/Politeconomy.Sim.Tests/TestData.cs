using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Tests;

/// <summary>Настоящие данные из data/ — читаются один раз на все тесты.</summary>
public static class TestData
{
    static readonly Lazy<GameData> Data = new(() =>
    {
        var dir = DataDirectory.Find(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("Не найдена папка data/ выше каталога тестов");
        return GameData.Load(dir);
    });

    public static GameData Game => Data.Value;
}

/// <summary>Тестовая система: случайно меняет население — проверяет, что случайность воспроизводима.</summary>
public sealed class RandomWalkSystem(SimFrequency frequency = SimFrequency.Daily) : ISimSystem
{
    public string Name => "Случайное блуждание";
    public SimPhase Phase => SimPhase.Demography;
    public SimFrequency Frequency => frequency;

    public void Run(World world)
    {
        foreach (var c in world.Countries)
        {
            var old = c.Stats["population"];
            c.Stats["population"] = old with { Value = old.Value * (1 + (world.Random.NextDouble() - 0.5) * 1e-4), Source = StatSources.Model };
        }
    }
}

/// <summary>Записывает, в каком порядке и когда её запускали.</summary>
public sealed class RecordingSystem(string name, SimPhase phase, SimFrequency frequency, List<string> log) : ISimSystem
{
    public string Name => name;
    public SimPhase Phase => phase;
    public SimFrequency Frequency => frequency;
    public void Run(World world) => log.Add($"{world.Date:yyyy-MM-dd} {name}");
}
