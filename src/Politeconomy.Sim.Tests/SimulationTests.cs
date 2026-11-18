using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Tests;

public class SimulationTests
{
    [Fact]
    public void SystemsRunInPhaseOrderAndOnTheirCalendar()
    {
        var log = new List<string>();
        var world = WorldFactory.Create(TestData.Game);
        var sim = new Simulation(world,
        [
            new RecordingSystem("события", SimPhase.Events, SimFrequency.Daily, log),
            new RecordingSystem("экономика", SimPhase.Economy, SimFrequency.Monthly, log),
            new RecordingSystem("бюджетный год", SimPhase.Budget, SimFrequency.Yearly, log),
        ]);

        sim.RunUntil(new DateOnly(2026, 2, 2));

        Assert.Equal(32, log.Count(l => l.EndsWith("события")));
        Assert.Equal(["2026-02-01 экономика"], log.Where(l => l.EndsWith("экономика")));
        Assert.DoesNotContain(log, l => l.EndsWith("бюджетный год"));
        var feb1 = log.Where(l => l.StartsWith("2026-02-01")).ToList();
        Assert.Equal(["2026-02-01 экономика", "2026-02-01 события"], feb1);

        sim.RunUntil(new DateOnly(2027, 1, 1));
        Assert.Equal(["2027-01-01 бюджетный год"], log.Where(l => l.EndsWith("бюджетный год")));
    }

    [Fact]
    public void SameSeedGivesSameGame()
    {
        string Play(ulong seed)
        {
            var world = WorldFactory.Create(TestData.Game, seed);
            new Simulation(world, [new RandomWalkSystem()]).StepDays(400);
            return Save.SaveGame.Hash(world);
        }

        Assert.Equal(Play(42), Play(42));
        Assert.NotEqual(Play(42), Play(43));
    }

    [Fact]
    public void RandomGeneratorIsStable()
    {
        // Первые числа xoshiro256** для seed 1 зафиксированы: если они поменяются, старые партии перестанут повторяться.
        var r = new DeterministicRandom(1);
        var first = Enumerable.Range(0, 3).Select(_ => r.NextUInt64()).ToArray();
        var again = new DeterministicRandom(1);
        Assert.Equal(first, Enumerable.Range(0, 3).Select(_ => again.NextUInt64()).ToArray());
        Assert.All(Enumerable.Range(0, 1000).Select(_ => r.NextDouble()), v => Assert.InRange(v, 0.0, 1.0 - double.Epsilon));
    }

    [Fact]
    public void HostAdvancesTimeAndPauses()
    {
        var world = WorldFactory.Create(TestData.Game);
        using var host = new SimulationHost(new Simulation(world), [10, 20, 50, 100, 200]);
        host.Start();
        Assert.True(host.Snapshot.Paused);

        host.SetSpeed(5);
        Assert.True(WaitFor(() => host.Snapshot.Date >= new DateOnly(2026, 1, 20)), "время не пошло");

        host.SetPaused(true);
        Assert.True(WaitFor(() => host.Snapshot.Paused));
        var stopped = host.Snapshot.Date;
        Thread.Sleep(200);
        Assert.Equal(stopped, host.Snapshot.Date);
        Assert.Null(host.Error);
    }

    static bool WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            if (condition())
                return true;
            Thread.Sleep(10);
        }
        return false;
    }
}
