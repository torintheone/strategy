using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Text;

/// <summary>Текст подсказки «почему»: из чего сложился показатель и откуда он взят.</summary>
public static class Explain
{
    /// <summary>Год, начиная с которого данные считаются свежими для старта 2026 года.</summary>
    public const int FreshYear = 2023;

    public static string Stat(IndicatorDef def, StatValue? stat)
    {
        if (stat is null)
            return $"{def.Name}: нет данных\n{def.Description}";
        var lines = new List<string>
        {
            $"{def.Name}: {Fmt.Value(def.Format, stat.Value, def.Unit)}",
            def.Description,
        };
        if (stat.Factors is { Count: > 0 } factors)
        {
            lines.Add("");
            lines.Add("Из чего складывается:");
            foreach (var f in factors)
                lines.Add($"  {f.Label}: {Fmt.Value(f.Format, f.Value)}");
        }
        lines.Add("");
        lines.Add(Source(stat));
        if (stat.Year is { } year && year < FreshYear && stat.Source != StatSources.Model)
            lines.Add($"Данные устарели: последний доступный год — {year}.");
        return string.Join('\n', lines);
    }

    public static string Source(StatValue stat)
    {
        var year = stat.Year is { } y ? $", данные за {y} год" : "";
        if (stat.Source.StartsWith("wb:", StringComparison.Ordinal))
            return $"Источник: Всемирный банк, показатель {stat.Source[3..]}{year}{Note(stat)}.";
        if (stat.Source.StartsWith("bis:", StringComparison.Ordinal))
            return $"Источник: Банк международных расчётов, ключевые ставки центробанков, {stat.Method}.";
        if (stat.Source.StartsWith("imf:", StringComparison.Ordinal))
            return $"Источник: МВФ, World Economic Outlook, показатель {stat.Source[4..]}{year}{(stat.Method is null ? " (за последний год — оценка МВФ)" : Note(stat))}.";
        if (stat.Source == "natural_earth")
            return $"Источник: Natural Earth (по данным CIA World Factbook){year}. Других открытых данных по этой стране нет.";
        return stat.Source switch
        {
            StatSources.Fill => $"Оценка: в источниках нет данных по стране, взята {stat.Method} (стран в выборке: {stat.N}).",
            StatSources.Derived => $"Оценка: {stat.Method}.",
            StatSources.Model => "Рассчитано моделью игры по состоянию на текущий месяц.",
            _ => $"Источник: {stat.Source}.",
        };
    }

    static string Note(StatValue stat) => stat.Method is { } m ? $" ({m})" : "";
}
