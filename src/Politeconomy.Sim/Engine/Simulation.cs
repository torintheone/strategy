using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Engine;

/// <summary>Порядок систем внутри шага — фиксированный, описан в MODEL.md.</summary>
public enum SimPhase
{
    Laws = 5,
    Economy = 10,
    LaborMarket = 20,
    PricesAndInflation = 30,
    Budget = 40,
    Demography = 50,
    SocialSphere = 60,
    Politics = 70,
    Diplomacy = 80,
    AiDecisions = 90,
    Events = 100,
}

public enum SimFrequency
{
    Daily,
    Monthly,
    Yearly,
}

/// <summary>Одна подсистема модели: экономика, демография, политика и т. д.</summary>
public interface ISimSystem
{
    string Name { get; }
    SimPhase Phase { get; }
    SimFrequency Frequency { get; }
    void Run(World world);
}

/// <summary>Продвигает мир на один день и запускает системы по их календарю.</summary>
public sealed class Simulation
{
    readonly List<ISimSystem> _systems = [];

    public Simulation(World world, IEnumerable<ISimSystem>? systems = null)
    {
        World = world;
        foreach (var s in systems ?? [])
            Add(s);
    }

    public World World { get; }

    public IReadOnlyList<ISimSystem> Systems => _systems;

    public void Add(ISimSystem system)
    {
        _systems.Add(system);
        // Стабильная сортировка: при равной фазе сохраняется порядок добавления.
        var ordered = _systems.Select((s, i) => (s, i)).OrderBy(x => x.s.Phase).ThenBy(x => x.i).Select(x => x.s).ToList();
        _systems.Clear();
        _systems.AddRange(ordered);
    }

    /// <summary>Один игровой день. Месячные системы срабатывают 1-го числа, годовые — 1 января.</summary>
    public void StepDay()
    {
        World.Date = World.Date.AddDays(1);
        var newMonth = World.Date.Day == 1;
        var newYear = newMonth && World.Date.Month == 1;
        foreach (var system in _systems)
        {
            var due = system.Frequency switch
            {
                SimFrequency.Daily => true,
                SimFrequency.Monthly => newMonth,
                SimFrequency.Yearly => newYear,
                _ => false,
            };
            if (due)
                system.Run(World);
        }
    }

    public void StepDays(int days)
    {
        for (var i = 0; i < days; i++)
            StepDay();
    }

    /// <summary>Шагает до указанной даты включительно.</summary>
    public void RunUntil(DateOnly date)
    {
        while (World.Date < date)
            StepDay();
    }
}
