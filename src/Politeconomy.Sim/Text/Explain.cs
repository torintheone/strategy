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
            "",
            Source(stat),
        };
        if (stat.Year is { } year && year < FreshYear)
            lines.Add($"Данные устарели: последний доступный год — {year}.");
        return string.Join('\n', lines);
    }

    public static string Source(StatValue stat)
    {
        var year = stat.Year is { } y ? $", данные за {y} год" : "";
        if (stat.Source.StartsWith("wb:", StringComparison.Ordinal))
            return $"Источник: Всемирный банк, показатель {stat.Source[3..]}{year}.";
        if (stat.Source.StartsWith("imf:", StringComparison.Ordinal))
            return $"Источник: МВФ, World Economic Outlook, показатель {stat.Source[4..]}{year} (за последний год — оценка МВФ).";
        if (stat.Source == "natural_earth")
            return $"Источник: Natural Earth (по данным CIA World Factbook){year}. Других открытых данных по этой стране нет.";
        return stat.Source switch
        {
            StatSources.Fill => $"Оценка: в источниках нет данных по стране, взята {stat.Method} (стран в выборке: {stat.N}).",
            StatSources.Derived => $"Оценка: {stat.Method}.",
            StatSources.Model => "Рассчитано моделью игры.",
            _ => $"Источник: {stat.Source}.",
        };
    }
}
