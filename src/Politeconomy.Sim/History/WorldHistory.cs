namespace Politeconomy.Sim.History;

/// <summary>
/// История показателей по годам: на старте — данные за 10 лет до 2026 года, дальше каждый январь записываются
/// значения прошедшего года. Values[страна][показатель][i] — значение за год FirstYear + i (null — нет данных).
/// </summary>
public sealed class WorldHistory
{
    public int FirstYear { get; set; }
    public Dictionary<string, Dictionary<string, List<double?>>> Values { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Последний год, за который есть строка (одинаков для всех показателей).</summary>
    public int LastYear { get; set; }
}
