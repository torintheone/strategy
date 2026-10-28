using System.Collections.Concurrent;
using System.Diagnostics;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Engine;

/// <summary>
/// Запускает симуляцию в фоновом потоке. Интерфейс отправляет команды через <see cref="Post"/>
/// и читает <see cref="Snapshot"/>. Мир меняется только в потоке симуляции.
/// </summary>
public sealed class SimulationHost : IDisposable
{
    public const int MaxSpeed = 5;

    readonly Simulation _simulation;
    readonly IReadOnlyList<double> _daysPerSecond;
    readonly ConcurrentQueue<Action<World>> _commands = new();
    readonly AutoResetEvent _wake = new(false);
    readonly Thread _thread;
    volatile bool _stopping;
    volatile WorldSnapshot _snapshot;
    int _speed = 1;
    bool _paused = true;
    long _version;

    public SimulationHost(Simulation simulation, IReadOnlyList<double> daysPerSecond)
    {
        if (daysPerSecond.Count != MaxSpeed)
            throw new ArgumentException($"Нужно {MaxSpeed} скоростей", nameof(daysPerSecond));
        _simulation = simulation;
        _daysPerSecond = daysPerSecond;
        _snapshot = WorldSnapshot.Capture(simulation.World, _speed, _paused, _version);
        _thread = new Thread(Loop) { IsBackground = true, Name = "Politeconomy.Simulation" };
    }

    /// <summary>Последний опубликованный снимок мира.</summary>
    public WorldSnapshot Snapshot => _snapshot;

    /// <summary>Сколько миллисекунд занял последний игровой день (для отладки производительности).</summary>
    public double LastStepMs { get; private set; }

    /// <summary>Ошибка, остановившая поток симуляции, если она случилась.</summary>
    public Exception? Error { get; private set; }

    public void Start() => _thread.Start();

    /// <summary>Выполнить действие над миром в потоке симуляции перед следующим шагом.</summary>
    public void Post(Action<World> command)
    {
        _commands.Enqueue(command);
        _wake.Set();
    }

    public void SetPaused(bool paused) => Post(_ => _paused = paused);

    public void TogglePause() => Post(_ => _paused = !_paused);

    public void SetSpeed(int speed) => Post(_ =>
    {
        _speed = Math.Clamp(speed, 1, MaxSpeed);
        _paused = false;
    });

    void Loop()
    {
        var clock = Stopwatch.StartNew();
        var debt = 0.0; // накопленная доля дня, которую пора сыграть
        var last = clock.Elapsed.TotalSeconds;
        try
        {
            while (!_stopping)
            {
                var changed = DrainCommands();
                var now = clock.Elapsed.TotalSeconds;
                var dt = Math.Min(now - last, 0.25);
                last = now;

                if (_paused)
                {
                    debt = 0;
                    if (changed)
                        Publish();
                    _wake.WaitOne(50);
                    continue;
                }

                debt += dt * _daysPerSecond[_speed - 1];
                var stepped = false;
                while (debt >= 1 && !_stopping)
                {
                    var started = clock.Elapsed.TotalMilliseconds;
                    _simulation.StepDay();
                    LastStepMs = clock.Elapsed.TotalMilliseconds - started;
                    debt -= 1;
                    stepped = true;
                    if (DrainCommands() && _paused)
                        break;
                }
                if (stepped || changed)
                    Publish();
                _wake.WaitOne(10);
            }
        }
        catch (Exception e)
        {
            Error = e;
        }
    }

    bool DrainCommands()
    {
        var any = false;
        while (_commands.TryDequeue(out var command))
        {
            command(_simulation.World);
            any = true;
        }
        return any;
    }

    void Publish() => _snapshot = WorldSnapshot.Capture(_simulation.World, _speed, _paused, Interlocked.Increment(ref _version));

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        if (_thread.IsAlive)
            _thread.Join(1000);
        _wake.Dispose();
    }
}
